using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentHelm.Bridge.Security;

namespace AgentHelm.Bridge.Agents.Acp;

/// <summary>
/// Transport abstraction so the ACP client can be driven by a real agent
/// process in production and by scripted in-memory streams in tests.
/// </summary>
public interface IAcpTransport : IDisposable
{
    Task<string?> ReadLineAsync(CancellationToken ct);
    Task WriteLineAsync(string line, CancellationToken ct);
}

/// <summary>Spawns the agent as a child process and talks NDJSON over stdio.</summary>
public sealed class ProcessTransport : IAcpTransport
{
    private readonly Process _process;

    public ProcessTransport(string command, IReadOnlyList<string> args, string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            // No byte-order mark: Encoding.UTF8 would prefix the first message
            // (initialize) with EF BB BF, and an agent whose JSON parser rejects
            // that line never answers the handshake.
            StandardInputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        foreach (var (k, v) in environment ?? new Dictionary<string, string>()) psi.Environment[k] = v;

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start agent process '{command}'.");
        // Drain stderr in the background so the child never blocks on a full pipe.
        _ = Task.Run(async () =>
        {
            while (await _process.StandardError.ReadLineAsync() is { } line)
                StderrLine?.Invoke(line);
        });
    }

    public event Action<string>? StderrLine;
    public bool HasExited => _process.HasExited;

    public Task<string?> ReadLineAsync(CancellationToken ct) =>
        _process.StandardOutput.ReadLineAsync(ct).AsTask();

    public async Task WriteLineAsync(string line, CancellationToken ct)
    {
        await _process.StandardInput.WriteLineAsync(line.AsMemory(), ct);
        await _process.StandardInput.FlushAsync(ct);
    }

    public void Dispose()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch { /* already gone */ }
        _process.Dispose();
    }
}

/// <summary>
/// A prompt attachment. Kind "image" carries base64 in Data; kind "text"
/// carries plain text embedded as an ACP resource block.
/// </summary>
public sealed record PromptAttachment(string Kind, string Name, string MimeType, string Data);

/// <summary>Parsed agent capabilities from the initialize handshake.</summary>
public sealed record AgentCaps(bool LoadSession, bool Image, bool Audio, bool EmbeddedContext);

/// <summary>One streamed update from the agent (chunk of text, tool call, plan…).</summary>
public sealed record AcpUpdate(string SessionId, string Kind, JsonNode Payload);

/// <summary>A permission request the agent raised before running a tool.</summary>
public sealed record AcpPermissionRequest(
    string RequestKey,
    string SessionId,
    string ToolTitle,
    string ToolKind,
    JsonNode RawToolCall,
    IReadOnlyList<AcpPermissionOption> Options);

public sealed record AcpPermissionOption(string OptionId, string Name, string Kind);

/// <summary>
/// Minimal Agent Client Protocol client (agentclientprotocol.com).
/// JSON-RPC 2.0, one message per line (NDJSON), over the given transport.
///
/// Design notes:
/// - The protocol is young and agents differ in details, so parsing is
///   deliberately tolerant: unknown notification kinds surface as raw updates
///   instead of throwing, and unknown server requests get a clean
///   -32601 so a well-behaved agent can continue.
/// - fs/read_text_file and fs/write_text_file are implemented with a hard
///   path guard: the agent may only touch files under the session's working
///   directory. This client is the security boundary, not the UI.
/// </summary>
public sealed class AcpClient : IDisposable
{
    private readonly IAcpTransport _transport;
    private readonly string _cwd;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonNode?>> _pending = new();
    private long _nextId;
    private Task? _readLoop;

    public AcpClient(IAcpTransport transport, string workingDirectory, ILogger logger)
    {
        _transport = transport;
        _cwd = Path.GetFullPath(workingDirectory);
        _logger = logger;
    }

    /// <summary>Streamed session updates (message chunks, tool calls, plans…).</summary>
    public event Action<AcpUpdate>? OnUpdate;

    /// <summary>
    /// Invoked when the agent asks for permission to run a tool. Return the
    /// chosen optionId, or null to reject/cancel. If no handler is attached,
    /// requests are rejected — safe by default.
    /// </summary>
    public Func<AcpPermissionRequest, Task<string?>>? PermissionHandler { get; set; }

    public void Start()
    {
        _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    // ---------------------------------------------------------------- calls

    /// <summary>True after initialize if the agent can replay stored sessions (session/load).</summary>
    public bool SupportsLoadSession { get; private set; }

    /// <summary>Full capability set from initialize — surfaced to the UI as hints.</summary>
    public AgentCaps Capabilities { get; private set; } = new(false, false, false, false);

    public async Task<JsonNode?> InitializeAsync(CancellationToken ct = default)
    {
        var params_ = new JsonObject
        {
            ["protocolVersion"] = 1,
            ["clientCapabilities"] = new JsonObject
            {
                ["fs"] = new JsonObject { ["readTextFile"] = true, ["writeTextFile"] = true }
            }
        };
        var result = await RequestAsync("initialize", params_, ct);
        var caps = result?["agentCapabilities"];
        var promptCaps = caps?["promptCapabilities"];
        Capabilities = new AgentCaps(
            caps?["loadSession"]?.GetValue<bool>() ?? false,
            promptCaps?["image"]?.GetValue<bool>() ?? false,
            promptCaps?["audio"]?.GetValue<bool>() ?? false,
            promptCaps?["embeddedContext"]?.GetValue<bool>() ?? false);
        SupportsLoadSession = Capabilities.LoadSession;
        return result;
    }

    public async Task<string> NewSessionAsync(CancellationToken ct = default)
    {
        var params_ = new JsonObject
        {
            ["cwd"] = _cwd,
            ["mcpServers"] = new JsonArray()
        };
        var result = await RequestAsync("session/new", params_, ct)
            ?? throw new InvalidOperationException("session/new returned no result");
        return result["sessionId"]?.GetValue<string>()
            ?? throw new InvalidOperationException("session/new result has no sessionId");
    }

    /// <summary>
    /// Resumes an existing agent-side session (CLI &lt;-&gt; GUI continuity).
    /// The agent replays the stored conversation as session/update
    /// notifications BEFORE responding, so OnUpdate must be wired first.
    /// </summary>
    public async Task LoadSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (!SupportsLoadSession)
            throw new AcpException(-32601, "This agent does not support session/load.");
        var params_ = new JsonObject
        {
            ["sessionId"] = sessionId,
            ["cwd"] = _cwd,
            ["mcpServers"] = new JsonArray()
        };
        await RequestAsync("session/load", params_, ct);
    }

    /// <summary>Sends a prompt; resolves when the turn ends. Streaming arrives via OnUpdate.</summary>
    public async Task<string> PromptAsync(string sessionId, string text,
        IReadOnlyList<PromptAttachment>? attachments = null, CancellationToken ct = default)
    {
        var prompt = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text });
        foreach (var a in attachments ?? [])
        {
            prompt.Add(a.Kind == "image"
                ? new JsonObject { ["type"] = "image", ["data"] = a.Data, ["mimeType"] = a.MimeType }
                : new JsonObject
                {
                    ["type"] = "resource",
                    ["resource"] = new JsonObject
                    {
                        ["uri"] = $"file:///{a.Name}",
                        ["mimeType"] = a.MimeType,
                        ["text"] = a.Data
                    }
                });
        }
        var params_ = new JsonObject { ["sessionId"] = sessionId, ["prompt"] = prompt };
        var result = await RequestAsync("session/prompt", params_, ct);
        return result?["stopReason"]?.GetValue<string>() ?? "end_turn";
    }

    public Task CancelAsync(string sessionId, CancellationToken ct = default) =>
        NotifyAsync("session/cancel", new JsonObject { ["sessionId"] = sessionId }, ct);

    // ------------------------------------------------------------- plumbing

    private async Task<JsonNode?> RequestAsync(string method, JsonNode params_, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = params_
        };
        await _transport.WriteLineAsync(envelope.ToJsonString(), ct);

        await using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        return await tcs.Task;
    }

    private Task NotifyAsync(string method, JsonNode params_, CancellationToken ct)
    {
        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = params_
        };
        return _transport.WriteLineAsync(envelope.ToJsonString(), ct);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _transport.ReadLineAsync(ct);
                if (line is null) break;                 // agent exited
                if (string.IsNullOrWhiteSpace(line)) continue;

                JsonNode? msg;
                try { msg = JsonNode.Parse(line); }
                catch (JsonException)
                {
                    _logger.LogDebug("Non-JSON line from agent ignored: {Line}", Truncate(line));
                    continue;                            // agents sometimes log to stdout — tolerate
                }
                if (msg is null) continue;

                var hasId = msg["id"] is not null;
                var method = msg["method"]?.GetValue<string>();

                if (hasId && method is null)
                    HandleResponse(msg);
                else if (hasId && method is not null)
                    _ = Task.Run(() => HandleServerRequestAsync(msg, method, ct), ct);
                else if (method is not null)
                    HandleNotification(msg, method);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ACP read loop terminated unexpectedly.");
        }
        finally
        {
            foreach (var (_, tcs) in _pending)
                tcs.TrySetException(new IOException("Agent connection closed."));
            _pending.Clear();
        }
    }

    private void HandleResponse(JsonNode msg)
    {
        if (msg["id"] is not { } idNode || !_pending.TryRemove(idNode.GetValue<long>(), out var tcs))
            return;
        if (msg["error"] is { } error)
            tcs.TrySetException(new AcpException(
                error["code"]?.GetValue<int>() ?? -1,
                error["message"]?.GetValue<string>() ?? "agent error"));
        else
            tcs.TrySetResult(msg["result"]);
    }

    private void HandleNotification(JsonNode msg, string method)
    {
        if (method != "session/update")
        {
            _logger.LogDebug("Unhandled notification {Method}", method);
            return;
        }
        var params_ = msg["params"];
        var sessionId = params_?["sessionId"]?.GetValue<string>() ?? "";
        var update = params_?["update"];
        var kind = update?["sessionUpdate"]?.GetValue<string>() ?? "unknown";
        OnUpdate?.Invoke(new AcpUpdate(sessionId, kind, update ?? new JsonObject()));
    }

    private async Task HandleServerRequestAsync(JsonNode msg, string method, CancellationToken ct)
    {
        var id = msg["id"]!.DeepClone();
        var params_ = msg["params"];
        try
        {
            JsonNode? result = method switch
            {
                "session/request_permission" => await HandlePermissionAsync(params_!),
                "fs/read_text_file" => HandleReadFile(params_!),
                "fs/write_text_file" => HandleWriteFile(params_!),
                _ => throw new AcpException(-32601, $"Method not found: {method}")
            };
            var response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
            await _transport.WriteLineAsync(response.ToJsonString(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Every request gets an answer: an agent waiting for one that never
            // comes hangs its turn. A missing file is "resource not found";
            // anything else that is not a protocol error is an internal error.
            var (code, message) = ex switch
            {
                AcpException acp => (acp.Code, acp.Message),
                FileNotFoundException or DirectoryNotFoundException => (-32002, ex.Message),
                _ => (-32603, ex.Message)
            };
            var response = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
            };
            await _transport.WriteLineAsync(response.ToJsonString(), ct);
        }
    }

    private async Task<JsonNode> HandlePermissionAsync(JsonNode params_)
    {
        var sessionId = params_["sessionId"]?.GetValue<string>() ?? "";
        var toolCall = params_["toolCall"] ?? new JsonObject();
        var options = (params_["options"] as JsonArray ?? [])
            .OfType<JsonNode>()
            .Select(o => new AcpPermissionOption(
                o["optionId"]?.GetValue<string>() ?? "",
                o["name"]?.GetValue<string>() ?? "",
                o["kind"]?.GetValue<string>() ?? ""))
            .ToList();

        var request = new AcpPermissionRequest(
            Guid.NewGuid().ToString("N"),
            sessionId,
            toolCall["title"]?.GetValue<string>() ?? "tool",
            toolCall["kind"]?.GetValue<string>() ?? "other",
            toolCall,
            options);

        var chosen = PermissionHandler is null ? null : await PermissionHandler(request);

        // Rejected/cancelled → pick the reject option if the agent offered one.
        if (chosen is null)
        {
            var reject = options.FirstOrDefault(o => o.Kind.Contains("reject", StringComparison.OrdinalIgnoreCase));
            return reject is null
                ? new JsonObject { ["outcome"] = new JsonObject { ["outcome"] = "cancelled" } }
                : new JsonObject { ["outcome"] = new JsonObject { ["outcome"] = "selected", ["optionId"] = reject.OptionId } };
        }
        return new JsonObject { ["outcome"] = new JsonObject { ["outcome"] = "selected", ["optionId"] = chosen } };
    }

    private JsonNode HandleReadFile(JsonNode params_)
    {
        var path = GuardPath(params_["path"]?.GetValue<string>());
        var lines = File.ReadAllLines(path);
        var start = Math.Max(0, (params_["line"]?.GetValue<int>() ?? 1) - 1);
        var limit = params_["limit"]?.GetValue<int>() ?? lines.Length;
        var content = string.Join('\n', lines.Skip(start).Take(limit));
        return new JsonObject { ["content"] = content };
    }

    private JsonNode HandleWriteFile(JsonNode params_)
    {
        var path = GuardPath(params_["path"]?.GetValue<string>());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, params_["content"]?.GetValue<string>() ?? "");
        return new JsonObject();
    }

    /// <summary>
    /// The agent may only touch files under the session cwd — also where the
    /// path would leave it through a symbolic link. Non-negotiable.
    /// </summary>
    internal string GuardPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new AcpException(-32602, "path is required");
        return PathGuard.Resolve(_cwd, raw, allowRoot: true)
            ?? throw new AcpException(-32602, $"Access outside the session working directory is not allowed: {raw}");
    }

    private static string Truncate(string s) => s.Length > 200 ? s[..200] + "…" : s;

    public void Dispose()
    {
        _cts.Cancel();
        _transport.Dispose();
        _cts.Dispose();
    }
}

public sealed class AcpException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}
