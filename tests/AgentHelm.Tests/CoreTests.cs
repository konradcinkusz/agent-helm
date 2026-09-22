using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using AgentHelm.Bridge.Agents.Acp;
using AgentHelm.Bridge.Sessions;
using AgentHelm.Bridge.Workbench;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentHelm.Tests;

/// <summary>
/// Scripted in-memory transport: the test plays the agent. Lines written by
/// the client are captured; the test enqueues the agent's replies.
/// </summary>
public sealed class FakeTransport : IAcpTransport
{
    private readonly BlockingCollection<string> _fromAgent = [];
    public readonly ConcurrentQueue<JsonNode> Sent = new();
    public event Action<JsonNode>? OnClientLine;

    public void AgentSays(JsonNode node) => _fromAgent.Add(node.ToJsonString());
    public void CloseFromAgent() => _fromAgent.CompleteAdding();

    public Task<string?> ReadLineAsync(CancellationToken ct) => Task.Run<string?>(() =>
    {
        try { return _fromAgent.Take(ct); }
        catch (Exception) { return null; }
    }, ct);

    public Task WriteLineAsync(string line, CancellationToken ct)
    {
        var node = JsonNode.Parse(line)!;
        Sent.Enqueue(node);
        OnClientLine?.Invoke(node);
        return Task.CompletedTask;
    }

    public void Dispose() => _fromAgent.CompleteAdding();
}

public class AcpClientTests
{
    private static (AcpClient Client, FakeTransport Transport) Create(string? cwd = null)
    {
        var transport = new FakeTransport();
        var client = new AcpClient(transport, cwd ?? Path.GetTempPath(), NullLogger.Instance);
        client.Start();
        return (client, transport);
    }

    /// <summary>Auto-responds to a client request with the given result.</summary>
    private static void RespondTo(FakeTransport t, string method, JsonNode result) =>
        t.OnClientLine += node =>
        {
            if (node["method"]?.GetValue<string>() != method) return;
            t.AgentSays(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = node["id"]!.DeepClone(),
                ["result"] = result.DeepClone()
            });
        };

    [Fact]
    public async Task InitializeSendsProtocolVersionAndFsCapabilities()
    {
        var (client, transport) = Create();
        RespondTo(transport, "initialize", new JsonObject { ["protocolVersion"] = 1 });

        await client.InitializeAsync();

        var sent = transport.Sent.Single(n => n["method"]?.GetValue<string>() == "initialize");
        Assert.Equal(1, sent["params"]!["protocolVersion"]!.GetValue<int>());
        Assert.True(sent["params"]!["clientCapabilities"]!["fs"]!["readTextFile"]!.GetValue<bool>());
    }

    [Fact]
    public async Task NewSessionReturnsAgentAssignedId()
    {
        var (client, transport) = Create();
        RespondTo(transport, "session/new", new JsonObject { ["sessionId"] = "s-42" });

        var id = await client.NewSessionAsync();

        Assert.Equal("s-42", id);
        var sent = transport.Sent.Single(n => n["method"]?.GetValue<string>() == "session/new");
        Assert.False(string.IsNullOrEmpty(sent["params"]!["cwd"]!.GetValue<string>()));
    }

    [Fact]
    public async Task PromptStreamsChunksInOrderThenResolvesWithStopReason()
    {
        var (client, transport) = Create();
        var chunks = new List<string>();
        client.OnUpdate += u => { if (u.Kind == "agent_message_chunk") chunks.Add(u.Payload["content"]!["text"]!.GetValue<string>()); };

        transport.OnClientLine += node =>
        {
            if (node["method"]?.GetValue<string>() != "session/prompt") return;
            foreach (var text in new[] { "Hel", "lo!" })
                transport.AgentSays(new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "session/update",
                    ["params"] = new JsonObject
                    {
                        ["sessionId"] = "s-1",
                        ["update"] = new JsonObject
                        {
                            ["sessionUpdate"] = "agent_message_chunk",
                            ["content"] = new JsonObject { ["type"] = "text", ["text"] = text }
                        }
                    }
                });
            transport.AgentSays(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = node["id"]!.DeepClone(),
                ["result"] = new JsonObject { ["stopReason"] = "end_turn" }
            });
        };

        var stop = await client.PromptAsync("s-1", "hi");

        Assert.Equal("end_turn", stop);
        Assert.Equal(new[] { "Hel", "lo!" }, chunks);
    }

    [Fact]
    public async Task PermissionRequestIsRoutedToHandlerAndAnswerWrittenBack()
    {
        var (client, transport) = Create();
        client.PermissionHandler = ask =>
        {
            Assert.Equal("write_file", ask.ToolTitle);
            Assert.Equal(2, ask.Options.Count);
            return Task.FromResult<string?>("allow-once");
        };

        transport.AgentSays(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 900,
            ["method"] = "session/request_permission",
            ["params"] = new JsonObject
            {
                ["sessionId"] = "s-1",
                ["toolCall"] = new JsonObject { ["title"] = "write_file", ["kind"] = "edit" },
                ["options"] = new JsonArray(
                    new JsonObject { ["optionId"] = "allow-once", ["name"] = "Allow", ["kind"] = "allow_once" },
                    new JsonObject { ["optionId"] = "reject-once", ["name"] = "Reject", ["kind"] = "reject_once" })
            }
        });

        var response = await WaitForSentAsync(transport,
            n => n["id"]?.GetValue<long>() == 900 && n["result"] is not null);
        Assert.Equal("selected", response["result"]!["outcome"]!["outcome"]!.GetValue<string>());
        Assert.Equal("allow-once", response["result"]!["outcome"]!["optionId"]!.GetValue<string>());
    }

    [Fact]
    public async Task MissingPermissionHandlerRejectsSafely()
    {
        var (client, transport) = Create();   // no handler attached

        transport.AgentSays(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 901,
            ["method"] = "session/request_permission",
            ["params"] = new JsonObject
            {
                ["sessionId"] = "s-1",
                ["toolCall"] = new JsonObject { ["title"] = "rm_rf", ["kind"] = "execute" },
                ["options"] = new JsonArray(
                    new JsonObject { ["optionId"] = "yes", ["name"] = "Allow", ["kind"] = "allow_once" },
                    new JsonObject { ["optionId"] = "no", ["name"] = "Reject", ["kind"] = "reject_once" })
            }
        });

        var response = await WaitForSentAsync(transport, n => n["id"]?.GetValue<long>() == 901);
        Assert.Equal("no", response["result"]!["outcome"]!["optionId"]!.GetValue<string>());
    }

    [Fact]
    public async Task FileReadOutsideCwdIsRefused()
    {
        var cwd = Directory.CreateTempSubdirectory().FullName;
        var (client, transport) = Create(cwd);

        transport.AgentSays(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 902,
            ["method"] = "fs/read_text_file",
            ["params"] = new JsonObject { ["sessionId"] = "s-1", ["path"] = "/etc/passwd" }
        });

        var response = await WaitForSentAsync(transport, n => n["id"]?.GetValue<long>() == 902);
        Assert.NotNull(response["error"]);
        Assert.Contains("not allowed", response["error"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task FileReadInsideCwdReturnsContent()
    {
        var cwd = Directory.CreateTempSubdirectory().FullName;
        await File.WriteAllTextAsync(Path.Combine(cwd, "hello.txt"), "line1\nline2\nline3");
        var (client, transport) = Create(cwd);

        transport.AgentSays(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 903,
            ["method"] = "fs/read_text_file",
            ["params"] = new JsonObject { ["sessionId"] = "s-1", ["path"] = "hello.txt", ["line"] = 2, ["limit"] = 1 }
        });

        var response = await WaitForSentAsync(transport, n => n["id"]?.GetValue<long>() == 903);
        Assert.Equal("line2", response["result"]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task AgentErrorResponseSurfacesAsAcpException()
    {
        var (client, transport) = Create();
        transport.OnClientLine += node =>
        {
            if (node["method"]?.GetValue<string>() != "session/new") return;
            transport.AgentSays(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = node["id"]!.DeepClone(),
                ["error"] = new JsonObject { ["code"] = -32000, ["message"] = "agent exploded" }
            });
        };

        var ex = await Assert.ThrowsAsync<AcpException>(() => client.NewSessionAsync());
        Assert.Equal(-32000, ex.Code);
    }

    [Fact]
    public async Task CancelSendsNotificationWithoutId()
    {
        var (client, transport) = Create();
        await client.CancelAsync("s-1");

        var sent = transport.Sent.Single(n => n["method"]?.GetValue<string>() == "session/cancel");
        Assert.Null(sent["id"]);
        Assert.Equal("s-1", sent["params"]!["sessionId"]!.GetValue<string>());
    }

    private static async Task<JsonNode> WaitForSentAsync(FakeTransport t, Func<JsonNode, bool> match)
    {
        for (var i = 0; i < 100; i++)
        {
            if (t.Sent.FirstOrDefault(match) is { } found) return found;
            await Task.Delay(20);
        }
        throw new TimeoutException("Expected client line was never written.");
    }
}

public class HelmSessionTests
{
    public sealed class FakeAdapter : IAgentAdapter
    {
        public event Action<AgentEvent>? OnEvent;
        public Func<PermissionAsk, Task<string?>>? PermissionHandler { get; set; }
        public string? NativeSessionId => "fake-native-1";
        public AgentCaps? Capabilities => new(true, true, false, true);
        public string? LastPrompt;

        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

        public IReadOnlyList<PromptAttachment>? LastAttachments;

        public async Task<string> PromptAsync(string text, IReadOnlyList<PromptAttachment>? attachments, CancellationToken ct)
        {
            LastPrompt = text;
            LastAttachments = attachments;
            OnEvent?.Invoke(new AgentEvent("assistant_chunk", "Hello "));
            OnEvent?.Invoke(new AgentEvent("assistant_chunk", "world"));
            if (text.Contains("tool"))
            {
                OnEvent?.Invoke(new AgentEvent("tool_call", "demo_tool"));
                var granted = PermissionHandler is null ? null : await PermissionHandler(
                    new PermissionAsk("req-1", "demo_tool", "edit",
                        [("go", "Allow", "allow_once"), ("no", "Reject", "reject_once")], "{}"));
                OnEvent?.Invoke(new AgentEvent("assistant_chunk", granted is null ? " denied" : " granted"));
            }
            return "end_turn";
        }

        public Task CancelAsync(CancellationToken ct) => Task.CompletedTask;
        public void Dispose() { }
    }

    private static HelmSession NewSession(FakeAdapter adapter)
    {
        var session = new HelmSession
        {
            Id = "t1", AgentId = "fake", Cwd = "/tmp", Adapter = adapter
        };
        session.WireAdapter();
        return session;
    }

    [Fact]
    public async Task PromptProducesUserAndAssistantEntries()
    {
        var session = NewSession(new FakeAdapter());
        await session.RunPromptAsync("hi", NullLogger.Instance);

        var transcript = session.TranscriptSnapshot();
        Assert.Equal("user", transcript[0].Role);
        Assert.Equal("hi", transcript[0].Text);
        Assert.Equal("assistant", transcript[^1].Role);
        Assert.Equal("Hello world", transcript[^1].Text);
        Assert.Equal("idle", session.Status);
    }

    [Fact]
    public async Task PermissionFlowBlocksUntilResolvedAndIsAudited()
    {
        var session = NewSession(new FakeAdapter());

        var run = Task.Run(() => session.RunPromptAsync("use the tool please", NullLogger.Instance));

        // Wait for the pending permission to surface…
        for (var i = 0; i < 100 && session.Pending is null; i++) await Task.Delay(20);
        Assert.NotNull(session.Pending);
        Assert.Equal("demo_tool", session.Pending!.ToolTitle);

        // …resolve it like the API endpoint would.
        Assert.True(session.ResolvePermission(session.Pending.RequestKey, "go"));
        await run;

        var transcript = session.TranscriptSnapshot();
        Assert.Contains(transcript, e => e.Kind == "permission");
        Assert.Contains(transcript, e => e.Kind == "permission_result" && e.Text.Contains("granted"));
        Assert.Contains(transcript, e => e.Role == "assistant" && e.Text.Contains("granted"));
        Assert.Null(session.Pending);
    }

    [Fact]
    public async Task SubscribersReceiveLiveEventsAndUnsubscribeStops()
    {
        var session = NewSession(new FakeAdapter());
        var reader = session.Subscribe(out var token);

        await session.RunPromptAsync("hi", NullLogger.Instance);

        var kinds = new List<string>();
        while (reader.TryRead(out var e)) kinds.Add(e.Kind);
        Assert.Contains("status", kinds);
        Assert.Contains("chunk", kinds);
        Assert.Contains("entry", kinds);
        Assert.Contains("turn_end", kinds);

        session.Unsubscribe(token);
        Assert.False(session.Subscribe(out _).Completion.IsCompleted);
    }
}

public class PolicyEngineTests
{
    private static PermissionAsk Ask(string kind, params (string, string, string)[] options) =>
        new("rk", "some_tool", kind, options, "{}");

    private static readonly (string, string, string)[] StandardOptions =
        [("a-once", "Allow", "allow_once"), ("r-once", "Reject", "reject_once")];

    [Fact]
    public void AskPolicyAlwaysAsks()
    {
        var decision = PolicyEngine.Decide(Ask("read", StandardOptions), PermissionPolicies.Ask);
        Assert.False(decision.IsAuto);
    }

    [Fact]
    public void YoloAutoAllowsAnyKind()
    {
        foreach (var kind in new[] { "read", "edit", "execute", "delete", "other" })
        {
            var decision = PolicyEngine.Decide(Ask(kind, StandardOptions), PermissionPolicies.Yolo);
            Assert.True(decision.IsAuto, $"yolo should auto-allow kind '{kind}'");
            Assert.Equal("a-once", decision.OptionId);
        }
    }

    [Fact]
    public void AutoReadAllowsReadOnlyKindsOnly()
    {
        Assert.True(PolicyEngine.Decide(Ask("read", StandardOptions), PermissionPolicies.AutoRead).IsAuto);
        Assert.True(PolicyEngine.Decide(Ask("search", StandardOptions), PermissionPolicies.AutoRead).IsAuto);
        Assert.False(PolicyEngine.Decide(Ask("edit", StandardOptions), PermissionPolicies.AutoRead).IsAuto);
        Assert.False(PolicyEngine.Decide(Ask("execute", StandardOptions), PermissionPolicies.AutoRead).IsAuto);
    }

    [Fact]
    public void AutoReadDoesNotAutoAllowNetworkFetch()
    {
        // fetch can exfiltrate what a read just loaded — deliberately not auto.
        Assert.False(PolicyEngine.Decide(Ask("fetch", StandardOptions), PermissionPolicies.AutoRead).IsAuto);
    }

    [Fact]
    public void PrefersAllowOnceOverAllowAlways()
    {
        var ask = Ask("edit",
            ("a-always", "Always allow", "allow_always"),
            ("a-once", "Allow once", "allow_once"),
            ("r-once", "Reject", "reject_once"));
        var decision = PolicyEngine.Decide(ask, PermissionPolicies.Yolo);
        Assert.Equal("a-once", decision.OptionId);
    }

    [Fact]
    public void NoAllowOptionFallsBackToAsking()
    {
        var ask = Ask("edit", ("r-once", "Reject", "reject_once"));
        Assert.False(PolicyEngine.Decide(ask, PermissionPolicies.Yolo).IsAuto);
    }

    [Fact]
    public void PolicyValidation()
    {
        Assert.True(PermissionPolicies.IsValid("ask"));
        Assert.True(PermissionPolicies.IsValid("auto_read"));
        Assert.True(PermissionPolicies.IsValid("yolo"));
        Assert.False(PermissionPolicies.IsValid("chaos"));
        Assert.False(PermissionPolicies.IsValid(null));
    }
}

public class HelmSessionPolicyTests
{
    private sealed class ToolAdapter : IAgentAdapter
    {
        public event Action<AgentEvent>? OnEvent;
        public Func<PermissionAsk, Task<string?>>? PermissionHandler { get; set; }
        public string? NativeSessionId => "native-42";
        public AgentCaps? Capabilities => null;

        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

        public async Task<string> PromptAsync(string text, IReadOnlyList<PromptAttachment>? attachments, CancellationToken ct)
        {
            var granted = PermissionHandler is null ? null : await PermissionHandler(
                new PermissionAsk("req-x", "shell_command", "execute",
                    [("go", "Allow", "allow_once"), ("no", "Reject", "reject_once")], "{}"));
            OnEvent?.Invoke(new AgentEvent("assistant_chunk", granted is null ? "denied" : "ran it"));
            return "end_turn";
        }

        public Task CancelAsync(CancellationToken ct) => Task.CompletedTask;
        public void Dispose() { }
    }

    private static HelmSession NewSession()
    {
        var session = new HelmSession { Id = "p1", AgentId = "fake", Cwd = "/tmp", Adapter = new ToolAdapter() };
        session.WireAdapter();
        return session;
    }

    [Fact]
    public async Task YoloAutoAllowsWithoutPendingAndAudits()
    {
        var session = NewSession();
        Assert.True(session.SetPolicy(PermissionPolicies.Yolo));

        await session.RunPromptAsync("run something", NullLogger.Instance);

        Assert.Null(session.Pending);
        var transcript = session.TranscriptSnapshot();
        Assert.Contains(transcript, e => e.Kind == "policy");
        Assert.Contains(transcript, e => e.Kind == "permission_auto" && e.Text.Contains("yolo"));
        Assert.DoesNotContain("\"permission\",", string.Join(",", transcript.Select(e => $"\"{e.Kind}\"")));
        Assert.Contains(transcript, e => e.Role == "assistant" && e.Text.Contains("ran it"));
    }

    [Fact]
    public async Task AskPolicyStillSurfacesPending()
    {
        var session = NewSession();   // default: ask
        var run = Task.Run(() => session.RunPromptAsync("run something", NullLogger.Instance));

        for (var i = 0; i < 100 && session.Pending is null; i++) await Task.Delay(20);
        Assert.NotNull(session.Pending);
        session.ResolvePermission(session.Pending!.RequestKey, "go");
        await run;
    }

    [Fact]
    public void SetPolicyValidatesAndDeduplicates()
    {
        var session = NewSession();
        Assert.False(session.SetPolicy("chaos"));
        Assert.True(session.SetPolicy(PermissionPolicies.AutoRead));
        Assert.False(session.SetPolicy(PermissionPolicies.AutoRead));   // no-op, no duplicate audit
        Assert.Equal(1, session.TranscriptSnapshot().Count(e => e.Kind == "policy"));
    }
}

public class AcpLoadSessionTests
{
    [Fact]
    public async Task InitializeReadsLoadSessionCapability()
    {
        var transport = new FakeTransport();
        var client = new AcpClient(transport, Path.GetTempPath(), NullLogger.Instance);
        client.Start();
        transport.OnClientLine += node =>
        {
            if (node["method"]?.GetValue<string>() != "initialize") return;
            transport.AgentSays(new System.Text.Json.Nodes.JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = node["id"]!.DeepClone(),
                ["result"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["protocolVersion"] = 1,
                    ["agentCapabilities"] = new System.Text.Json.Nodes.JsonObject { ["loadSession"] = true }
                }
            });
        };

        await client.InitializeAsync();
        Assert.True(client.SupportsLoadSession);
    }

    [Fact]
    public async Task LoadSessionReplaysHistoryThenCompletes()
    {
        var transport = new FakeTransport();
        var client = new AcpClient(transport, Path.GetTempPath(), NullLogger.Instance);
        var chunks = new List<string>();
        client.OnUpdate += u => { if (u.Kind == "agent_message_chunk") chunks.Add(u.Payload["content"]!["text"]!.GetValue<string>()); };
        client.Start();

        transport.OnClientLine += node =>
        {
            var method = node["method"]?.GetValue<string>();
            if (method == "initialize")
                transport.AgentSays(new System.Text.Json.Nodes.JsonObject
                {
                    ["jsonrpc"] = "2.0", ["id"] = node["id"]!.DeepClone(),
                    ["result"] = new System.Text.Json.Nodes.JsonObject
                    { ["agentCapabilities"] = new System.Text.Json.Nodes.JsonObject { ["loadSession"] = true } }
                });
            if (method == "session/load")
            {
                transport.AgentSays(new System.Text.Json.Nodes.JsonObject
                {
                    ["jsonrpc"] = "2.0", ["method"] = "session/update",
                    ["params"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["sessionId"] = "s-9",
                        ["update"] = new System.Text.Json.Nodes.JsonObject
                        {
                            ["sessionUpdate"] = "agent_message_chunk",
                            ["content"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "text", ["text"] = "replayed history" }
                        }
                    }
                });
                transport.AgentSays(new System.Text.Json.Nodes.JsonObject
                {
                    ["jsonrpc"] = "2.0", ["id"] = node["id"]!.DeepClone(), ["result"] = null
                });
            }
        };

        await client.InitializeAsync();
        await client.LoadSessionAsync("s-9");

        Assert.Contains("replayed history", chunks);
        var sent = transport.Sent.Single(n => n["method"]?.GetValue<string>() == "session/load");
        Assert.Equal("s-9", sent["params"]!["sessionId"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(sent["params"]!["cwd"]!.GetValue<string>()));
    }

    [Fact]
    public async Task LoadSessionWithoutCapabilityThrows()
    {
        var transport = new FakeTransport();
        var client = new AcpClient(transport, Path.GetTempPath(), NullLogger.Instance);
        client.Start();
        transport.OnClientLine += node =>
        {
            if (node["method"]?.GetValue<string>() != "initialize") return;
            transport.AgentSays(new System.Text.Json.Nodes.JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = node["id"]!.DeepClone(),
                ["result"] = new System.Text.Json.Nodes.JsonObject { ["protocolVersion"] = 1 }
            });
        };

        await client.InitializeAsync();
        var ex = await Assert.ThrowsAsync<AcpException>(() => client.LoadSessionAsync("s-1"));
        Assert.Contains("does not support", ex.Message);
    }
}

public class AttachmentTests
{
    [Fact]
    public async Task PromptBuildsImageAndResourceBlocks()
    {
        var transport = new FakeTransport();
        var client = new AcpClient(transport, Path.GetTempPath(), NullLogger.Instance);
        client.Start();
        transport.OnClientLine += node =>
        {
            if (node["method"]?.GetValue<string>() != "session/prompt") return;
            transport.AgentSays(new System.Text.Json.Nodes.JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = node["id"]!.DeepClone(),
                ["result"] = new System.Text.Json.Nodes.JsonObject { ["stopReason"] = "end_turn" }
            });
        };

        await client.PromptAsync("s-1", "look at this",
            new List<PromptAttachment>
            {
                new("image", "shot.png", "image/png", "aWJhc2U2NA=="),
                new("text", "notes.txt", "text/plain", "line one")
            }.AsReadOnly());

        var sent = transport.Sent.Single(n => n["method"]?.GetValue<string>() == "session/prompt");
        var prompt = sent["params"]!["prompt"]!.AsArray();
        Assert.Equal(3, prompt.Count);
        Assert.Equal("text", prompt[0]!["type"]!.GetValue<string>());
        Assert.Equal("image", prompt[1]!["type"]!.GetValue<string>());
        Assert.Equal("image/png", prompt[1]!["mimeType"]!.GetValue<string>());
        Assert.Equal("resource", prompt[2]!["type"]!.GetValue<string>());
        Assert.Equal("line one", prompt[2]!["resource"]!["text"]!.GetValue<string>());
        Assert.Contains("notes.txt", prompt[2]!["resource"]!["uri"]!.GetValue<string>());
    }

    [Fact]
    public async Task SessionNotesAttachmentsInUserEntryAndPassesThem()
    {
        var adapter = new HelmSessionTests.FakeAdapter();
        var session = new HelmSession { Id = "a1", AgentId = "fake", Cwd = "/tmp", Adapter = adapter };
        session.WireAdapter();

        await session.RunPromptAsync("check these", NullLogger.Instance,
            [new PromptAttachment("image", "diagram.png", "image/png", "eA==")]);

        var userEntry = session.TranscriptSnapshot().First(e => e.Role == "user");
        Assert.Contains("diagram.png", userEntry.Text);
        Assert.NotNull(adapter.LastAttachments);
        Assert.Equal(1, adapter.LastAttachments!.Count);
    }
}

public class GitServiceTests
{
    private sealed class FakeGitRunner : IGitRunner
    {
        public readonly List<string[]> Calls = [];
        public Func<string[], GitResult> Handler = _ => new GitResult(0, "", "");
        public Task<GitResult> RunAsync(string cwd, string[] args, CancellationToken ct)
        {
            Calls.Add(args);
            return Task.FromResult(Handler(args));
        }
    }

    [Fact]
    public void ParsesPorcelainIncludingRenameTokenConsumption()
    {
        // "R  new.txt" is followed by the ORIGINAL path as its own NUL token —
        // a naive parser would misread "old.txt" as another changed file.
        var raw = "M  a.txt\0?? b.txt\0R  new.txt\0old.txt\0 D gone.txt\0";
        var changes = GitService.ParsePorcelain(raw);

        Assert.Equal(4, changes.Count);
        Assert.Equal("modified", changes[0].Status);
        Assert.True(changes[1].Untracked);
        Assert.Equal("renamed", changes[2].Status);
        Assert.Equal("new.txt", changes[2].Path);
        Assert.Equal("deleted", changes[3].Status);
        Assert.Equal("gone.txt", changes[3].Path);
    }

    [Fact]
    public void CountsAdditionsAndDeletionsIgnoringHeaders()
    {
        var diff = "--- a/f\n+++ b/f\n@@ -1,2 +1,2 @@\n-old line\n+new line\n+another\n context";
        var (add, del) = GitService.CountChanges(diff);
        Assert.Equal(2, add);
        Assert.Equal(1, del);
    }

    [Fact]
    public void GuardPathRefusesEscapes()
    {
        var cwd = Directory.CreateTempSubdirectory().FullName;
        GitService.GuardPath(cwd, "sub/file.txt");   // fine
        Assert.Contains("escapes",
            Record.Exception(() => GitService.GuardPath(cwd, "../outside.txt"))!.Message);
    }

    [Fact]
    public async Task RejectTrackedRunsCheckoutRejectUntrackedDeletes()
    {
        var cwd = Directory.CreateTempSubdirectory().FullName;
        var runner = new FakeGitRunner();
        var service = new GitService(runner);

        // Tracked: status says modified -> expect checkout HEAD -- path
        runner.Handler = args => args[0] == "status"
            ? new GitResult(0, " M f.txt\0", "")
            : new GitResult(0, "", "");
        await service.RejectAsync(cwd, "f.txt", CancellationToken.None);
        Assert.Contains(runner.Calls, c => c[0] == "checkout" && c[1] == "HEAD" && c[3] == "f.txt");

        // Untracked: status says ?? -> file is deleted, no git mutation
        var loose = Path.Combine(cwd, "loose.txt");
        await File.WriteAllTextAsync(loose, "x");
        runner.Calls.Clear();
        runner.Handler = args => args[0] == "status"
            ? new GitResult(0, "?? loose.txt\0", "")
            : new GitResult(0, "", "");
        await service.RejectAsync(cwd, "loose.txt", CancellationToken.None);
        Assert.False(File.Exists(loose));
        Assert.DoesNotContain("checkout", string.Join(",", runner.Calls.Select(c => c[0])));
    }

    [Fact]
    public async Task RealGitEndToEnd()
    {
        var probe = new ProcessGitRunner();
        try { if ((await probe.RunAsync(Path.GetTempPath(), ["--version"], CancellationToken.None)).ExitCode != 0) return; }
        catch { return; }   // no git on this machine — skip silently

        var cwd = Directory.CreateTempSubdirectory().FullName;
        var service = new GitService(probe);
        async Task Git(params string[] args) =>
            Assert.Equal(0, (await probe.RunAsync(cwd, args, CancellationToken.None)).ExitCode);

        await Git("init", "-q");
        await Git("config", "user.email", "t@t");
        await Git("config", "user.name", "t");
        await Git("config", "core.autocrlf", "false");
        var file = Path.Combine(cwd, "hello.txt");
        await File.WriteAllTextAsync(file, "one\n");

        Assert.True(await service.IsRepoAsync(cwd, CancellationToken.None));
        var untracked = await service.ChangesAsync(cwd, CancellationToken.None);
        Assert.Contains(untracked, c => c.Path == "hello.txt" && c.Untracked);

        await Git("add", ".");
        await Git("commit", "-q", "-m", "init");
        await File.WriteAllTextAsync(file, "one\ntwo\n");

        var diff = await service.DiffAsync(cwd, "hello.txt", CancellationToken.None);
        Assert.Equal(1, diff.Additions);

        await service.RejectAsync(cwd, "hello.txt", CancellationToken.None);
        Assert.Equal("one\n", await File.ReadAllTextAsync(file));
    }
}

public class TerminalServiceTests
{
    [Fact]
    public async Task ShellEchoRoundtrip()
    {
        if (OperatingSystem.IsWindows()) return;   // sandbox/CI is unix; windows path is identical logic

        using var terminal = new TerminalSession(Path.GetTempPath());
        var reader = terminal.Subscribe(out var token);
        var collected = new System.Text.StringBuilder();

        await terminal.WriteInputAsync("echo m2-terminal-ok", CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !collected.ToString().Contains("m2-terminal-ok"))
        {
            if (reader.TryRead(out var e)) collected.Append(e.Text);
            else await Task.Delay(50);
        }
        Assert.Contains("m2-terminal-ok", collected.ToString());

        var buffer = terminal.BufferSnapshot();
        Assert.Contains("m2-terminal-ok", buffer);
        terminal.Unsubscribe(token);
    }
}

public class CapabilityTests
{
    [Fact]
    public async Task InitializeParsesPromptCapabilities()
    {
        var transport = new FakeTransport();
        var client = new AcpClient(transport, Path.GetTempPath(), NullLogger.Instance);
        client.Start();
        transport.OnClientLine += node =>
        {
            if (node["method"]?.GetValue<string>() != "initialize") return;
            transport.AgentSays(new System.Text.Json.Nodes.JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = node["id"]!.DeepClone(),
                ["result"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["agentCapabilities"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["loadSession"] = true,
                        ["promptCapabilities"] = new System.Text.Json.Nodes.JsonObject
                        { ["image"] = true, ["embeddedContext"] = true }
                    }
                }
            });
        };

        await client.InitializeAsync();

        Assert.True(client.Capabilities.LoadSession);
        Assert.True(client.Capabilities.Image);
        Assert.False(client.Capabilities.Audio);
        Assert.True(client.Capabilities.EmbeddedContext);
    }
}

public class AgentFactoryTests
{
    private static SessionManager Manager(string agentId, string type)
    {
        var dict = new Dictionary<string, string?>
        {
            [$"AgentHelm:Agents:0:Id"] = agentId,
            [$"AgentHelm:Agents:0:Name"] = agentId,
            [$"AgentHelm:Agents:0:Command"] = "true",
            [$"AgentHelm:Agents:0:Type"] = type,
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        return new SessionManager(new AgentCatalog(config), NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task UnknownAgentTypeFailsWithClearMessage()
    {
        var manager = Manager("weird", "quantum");
        var cwd = Directory.CreateTempSubdirectory().FullName;
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => manager.CreateAsync("weird", cwd, null, CancellationToken.None));
        Assert.Contains("Unknown agent type", ex.Message);
    }

    [Fact]
    public async Task CopilotSdkTypeWithoutFlagExplainsActivation()
    {
        var manager = Manager("cop", "copilot-sdk");
        var cwd = Directory.CreateTempSubdirectory().FullName;
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => manager.CreateAsync("cop", cwd, null, CancellationToken.None));
        Assert.Contains("COPILOT_SDK", ex.Message);
    }
}

public class HandoffTests
{
    [Fact]
    public async Task ContextContainsConversationAndAttribution()
    {
        var session = new HelmSession
        { Id = "h1", AgentId = "copilot", Cwd = "/repo/x", Adapter = new HelmSessionTests.FakeAdapter() };
        session.WireAdapter();
        await session.RunPromptAsync("fix the login bug", NullLogger.Instance);

        var context = SessionManager.BuildHandoffContext(session);

        Assert.Contains("copilot", context);
        Assert.Contains("/repo/x", context);
        Assert.Contains("user: fix the login bug", context);
        Assert.Contains("assistant: Hello world", context);
        Assert.Contains("continue", context.ToLowerInvariant());
    }

    [Fact]
    public async Task LongTranscriptTrimsFromFrontKeepingRecent()
    {
        var session = new HelmSession
        { Id = "h2", AgentId = "a", Cwd = "/x", Adapter = new HelmSessionTests.FakeAdapter() };
        session.WireAdapter();
        for (var i = 0; i < 60; i++)
            await session.RunPromptAsync($"prompt number {i} " + new string('x', 200), NullLogger.Instance);

        var context = SessionManager.BuildHandoffContext(session, maxChars: 3000);

        Assert.True(context.Length < 3600, $"context too long: {context.Length}");
        Assert.Contains("trimmed", context);
        Assert.Contains("prompt number 59", context);          // recency wins
        Assert.DoesNotContain("prompt number 0 ", context);    // oldest gone
    }
}

public class ScopeIntegrationTests
{
    [Fact]
    public void ParsesNestedAndFlatQualityShapes()
    {
        var json = """
        [
          {"id":"s1","title":"gpt-4o","quality":{"score":91.5,"grade":"excellent","confidence":0.42},"lastActivity":"2026-07-17T06:00:00Z"},
          {"sessionId":"s2","score":57,"grade":"fair","updatedAt":"2026-07-17T05:00:00Z"},
          {"noId":"skipped"}
        ]
        """;
        var parsed = AgentHelm.Bridge.Integrations.ScopeClient.ParseSessions(json);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("s1", parsed[0].Id);
        Assert.Equal(91.5, parsed[0].Score!.Value);
        Assert.Equal("excellent", parsed[0].Grade);
        Assert.Equal("s2", parsed[1].Id);
        Assert.Equal(57.0, parsed[1].Score!.Value);
        Assert.NotNull(parsed[1].LastActivity);
    }

    [Fact]
    public void CorrelateFiltersByWindowOrdersDescAndExcludesTimeless()
    {
        var now = DateTimeOffset.UtcNow;
        var sessions = new List<AgentHelm.Bridge.Integrations.ScopeSessionScore>
        {
            new("in-1", null, null, 90, null, null, now.AddMinutes(-5)),
            new("in-2", null, null, 80, null, null, now.AddMinutes(-1)),
            new("too-old", null, null, 70, null, null, now.AddHours(-3)),
            new("timeless", null, null, 60, null, null, null),
        };

        var matches = AgentHelm.Bridge.Integrations.ScopeClient.Correlate(
            sessions, now.AddMinutes(-10), now);

        Assert.Equal(2, matches.Count);
        Assert.Equal("in-2", matches[0].Id);   // newest first
        Assert.Equal("in-1", matches[1].Id);
    }
}

public class PtyTerminalTests
{
    [Fact]
    public async Task PtyModeEchoRoundtripWhenScriptAvailable()
    {
        if (OperatingSystem.IsWindows()) return;
        if (!File.Exists("/usr/bin/script") && !File.Exists("/bin/script")) return;

        using var terminal = new TerminalSession(Path.GetTempPath());
        Assert.True(terminal.IsPty);

        var reader = terminal.Subscribe(out var token);
        var collected = new System.Text.StringBuilder();
        await terminal.WriteInputAsync("echo m3-pty-ok", CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !collected.ToString().Contains("m3-pty-ok"))
        {
            if (reader.TryRead(out var e)) collected.Append(e.Text);
            else await Task.Delay(50);
        }
        Assert.Contains("m3-pty-ok", collected.ToString());
        terminal.Unsubscribe(token);
    }
}

public class SessionTitleTests
{
    [Fact]
    public void SetTitleValidatesPublishesAndSticks()
    {
        var session = new HelmSession
        { Id = "t9", AgentId = "fake", Cwd = "/tmp", Adapter = new HelmSessionTests.FakeAdapter() };
        session.WireAdapter();
        var reader = session.Subscribe(out _);

        Assert.False(session.SetTitle(""));
        Assert.False(session.SetTitle(new string('x', 121)));
        Assert.True(session.SetTitle("  Login bug hunt  "));
        Assert.Equal("Login bug hunt", session.Title);

        var sawTitleEvent = false;
        while (reader.TryRead(out var e)) sawTitleEvent |= e.Kind == "title";
        Assert.True(sawTitleEvent);
    }
}

public class SpecExpansionTests
{
    [Fact]
    public void ExpandsAgentHelmDirTokenInCommandAndArgs()
    {
        var spec = new AgentSpec("echo", "Echo", "dotnet")
        {
            Args = ["run", "--project", "${AGENTHELM_DIR}../tools/Echo"]
        };
        var (command, args) = AcpAdapter.ExpandSpecPaths(spec);

        Assert.Equal("dotnet", command);
        Assert.DoesNotContain("${AGENTHELM_DIR}", args[2]);
        Assert.Contains(AppContext.BaseDirectory, args[2]);
        Assert.EndsWith("../tools/Echo", args[2]);
        Assert.Equal("run", args[0]);   // untouched args stay untouched
    }
}

/// <summary>
/// The built-in echo agent as a real child process, driven by the real ACP
/// client — the path a first-time user takes. Scripted transports cannot see
/// the bytes on the wire or the agent's own concurrency; these tests can.
/// </summary>
public class EchoAgentEndToEndTests
{
    private static readonly string EchoAgentDll =
        Path.Combine(AppContext.BaseDirectory, "AgentHelm.EchoAgent.dll");

    private static AcpClient StartEcho()
    {
        var cwd = Path.GetTempPath();
        var client = new AcpClient(new ProcessTransport("dotnet", [EchoAgentDll], cwd), cwd, NullLogger.Instance);
        client.Start();
        return client;
    }

    [Fact]
    public async Task HandshakeCompletes()
    {
        using var client = StartEcho();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await client.InitializeAsync(timeout.Token);
        var sessionId = await client.NewSessionAsync(timeout.Token);

        Assert.True(client.Capabilities.LoadSession);
        Assert.StartsWith("echo-", sessionId);
    }

    [Theory]
    [InlineData("allow", "completed", "Tool ran with your blessing.")]
    [InlineData(null, "failed", "Tool was rejected")]
    public async Task PermissionDecisionLetsTheTurnFinish(string? answer, string toolStatus, string closingText)
    {
        using var client = StartEcho();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var updates = new ConcurrentQueue<AcpUpdate>();
        client.OnUpdate += updates.Enqueue;
        client.PermissionHandler = _ => Task.FromResult(answer);

        await client.InitializeAsync(timeout.Token);
        var sessionId = await client.NewSessionAsync(timeout.Token);
        var stopReason = await client.PromptAsync(sessionId, "please use a tool", null, timeout.Token);

        Assert.Equal("end_turn", stopReason);
        Assert.Contains(updates, u => u.Kind == "tool_call_update"
            && u.Payload["status"]?.GetValue<string>() == toolStatus);
        var reply = string.Concat(updates
            .Where(u => u.Kind == "agent_message_chunk")
            .Select(u => u.Payload["content"]?["text"]?.GetValue<string>()));
        Assert.Contains(closingText, reply);
    }
}

public class ProcessTransportTests
{
    [Fact]
    public async Task WritesNoByteOrderMark()
    {
        if (OperatingSystem.IsWindows()) return;   // relies on sh, head and od

        // Print the first three bytes received, in hex, then keep reading so the
        // writer never hits a closed pipe.
        using var transport = new ProcessTransport("sh",
            ["-c", "head -c 3 | od -An -tx1; cat > /dev/null"], Path.GetTempPath());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await transport.WriteLineAsync("""{"jsonrpc":"2.0"}""", timeout.Token);
        var firstBytes = await transport.ReadLineAsync(timeout.Token);

        Assert.Equal("7b 22 6a", firstBytes?.Trim());   // `{"j`, not EF BB BF
    }
}
