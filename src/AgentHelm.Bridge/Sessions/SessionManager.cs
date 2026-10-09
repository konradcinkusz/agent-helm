using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using AgentHelm.Bridge.Agents.Acp;

namespace AgentHelm.Bridge.Sessions;

// ---------------------------------------------------------------- contracts

/// <summary>
/// The seam that keeps AgentHelm multi-agent: every way of driving an agent
/// (ACP subprocess today, GitHub Copilot SDK in M1) implements this. The rest
/// of the Bridge — sessions, permissions, persistence, UI — never sees
/// protocol details.
/// </summary>
public interface IAgentAdapter : IDisposable
{
    /// <summary>The agent's own session id (ACP sessionId / SDK sessionId) — needed for resume.</summary>
    string? NativeSessionId { get; }

    /// <summary>Capability hints for the UI; null when the protocol has none.</summary>
    AgentCaps? Capabilities { get; }

    Task StartAsync(CancellationToken ct);
    Task<string> PromptAsync(string text, IReadOnlyList<PromptAttachment>? attachments, CancellationToken ct);
    Task CancelAsync(CancellationToken ct);
    event Action<AgentEvent>? OnEvent;
    Func<PermissionAsk, Task<string?>>? PermissionHandler { get; set; }
}

/// <summary>Uniform event surfaced to the session layer, whatever the protocol.</summary>
public sealed record AgentEvent(string Kind, string Text, JsonNode? Raw = null);

/// <summary>Uniform permission request, whatever the protocol.</summary>
public sealed record PermissionAsk(
    string RequestKey, string ToolTitle, string ToolKind,
    IReadOnlyList<(string OptionId, string Name, string Kind)> Options,
    string RawJson);

// ------------------------------------------------------------ agent catalog

/// <summary>One configured agent (from AgentHelm:Agents in appsettings).</summary>
/// <summary>One configured agent (from AgentHelm:Agents in appsettings).</summary>
/// <remarks>
/// Args is deliberately an init PROPERTY, not a constructor parameter: the
/// configuration binder silently drops records whose required ctor params
/// have no matching config section, so an agent declared without "Args"
/// would vanish from the catalog without a trace.
/// </remarks>
public sealed record AgentSpec(string Id, string Name, string Command)
{
    public List<string> Args { get; init; } = [];

    public Dictionary<string, string> Environment { get; init; } = new();

    /// <summary>Adapter type: "acp" (default) or "copilot-sdk" (requires COPILOT_SDK build).</summary>
    public string Type { get; init; } = "acp";
}

public sealed class AgentCatalog
{
    private readonly Dictionary<string, AgentSpec> _agents;

    public AgentCatalog(IConfiguration configuration)
    {
        _agents = configuration.GetSection("AgentHelm:Agents")
            .Get<List<AgentSpec>>()?.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, AgentSpec>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<AgentSpec> All => _agents.Values;
    public AgentSpec? Find(string id) => _agents.GetValueOrDefault(id);
}

// ------------------------------------------------------------- ACP adapter

/// <summary>Drives one agent session over the Agent Client Protocol.</summary>
public sealed class AcpAdapter : IAgentAdapter
{
    private readonly AgentSpec _spec;
    private readonly string _cwd;
    private readonly ILogger _logger;
    private readonly string? _resumeSessionId;
    private readonly string? _model;
    private AcpClient? _client;
    private string? _acpSessionId;

    public AcpAdapter(AgentSpec spec, string cwd, ILogger logger,
        string? resumeSessionId = null, string? model = null)
    {
        _spec = spec;
        _cwd = cwd;
        _logger = logger;
        _resumeSessionId = resumeSessionId;
        _model = model;
    }

    public string? NativeSessionId => _acpSessionId;
    public AgentCaps? Capabilities => _client?.Capabilities;

    public event Action<AgentEvent>? OnEvent;
    public Func<PermissionAsk, Task<string?>>? PermissionHandler { get; set; }

    /// <summary>
    /// Expands ${AGENTHELM_DIR} (the Bridge binaries directory) in agent
    /// command/args. Needed because agent processes start with the SESSION
    /// working directory as cwd — a relative path in the catalog would
    /// resolve against the user's repo, which is never what you meant.
    /// </summary>
    internal static (string Command, List<string> Args) ExpandSpecPaths(AgentSpec spec)
    {
        var baseDir = AppContext.BaseDirectory;
        string Expand(string value) => value.Replace("${AGENTHELM_DIR}", baseDir, StringComparison.Ordinal);
        return (Expand(spec.Command), spec.Args.Select(Expand).ToList());
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var (command, args) = ExpandSpecPaths(_spec);

        // Merge a model hint into the environment so the agent can pick it up.
        // Each CLI uses its own env var; we set what we know and the rest is ignored.
        var env = new Dictionary<string, string>(_spec.Environment);
        if (_model is not null)
        {
            env["GH_COPILOT_MODEL"] = _model;   // GitHub Copilot CLI
            env["CLAUDE_MODEL"]     = _model;   // Claude Code CLI
            env["GEMINI_MODEL"]     = _model;   // Gemini CLI
        }

        var transport = new ProcessTransport(command, args, _cwd, env);
        transport.StderrLine += line => _logger.LogDebug("[{Agent} stderr] {Line}", _spec.Id, line);

        _client = new AcpClient(transport, _cwd, _logger)
        {
            PermissionHandler = async acp =>
            {
                if (PermissionHandler is null) return null;
                var ask = new PermissionAsk(acp.RequestKey, acp.ToolTitle, acp.ToolKind,
                    acp.Options.Select(o => (o.OptionId, o.Name, o.Kind)).ToList(),
                    acp.RawToolCall.ToJsonString());
                return await PermissionHandler(ask);
            }
        };
        _client.OnUpdate += update => OnEvent?.Invoke(Translate(update));
        _client.Start();

        await _client.InitializeAsync(ct);
        if (_resumeSessionId is not null)
        {
            // Replayed history arrives as ordinary updates via OnUpdate.
            await _client.LoadSessionAsync(_resumeSessionId, ct);
            _acpSessionId = _resumeSessionId;
        }
        else
        {
            _acpSessionId = await _client.NewSessionAsync(ct);
        }
    }

    public Task<string> PromptAsync(string text, IReadOnlyList<PromptAttachment>? attachments, CancellationToken ct) =>
        _client is null || _acpSessionId is null
            ? throw new InvalidOperationException("Adapter not started.")
            : _client.PromptAsync(_acpSessionId, text, attachments, ct);

    public Task CancelAsync(CancellationToken ct) =>
        _client is null || _acpSessionId is null
            ? Task.CompletedTask
            : _client.CancelAsync(_acpSessionId, ct);

    /// <summary>Maps ACP update dialect onto the uniform event surface.</summary>
    private static AgentEvent Translate(AcpUpdate u) => u.Kind switch
    {
        "agent_message_chunk" => new AgentEvent("assistant_chunk",
            u.Payload["content"]?["text"]?.GetValue<string>() ?? ""),
        "agent_thought_chunk" => new AgentEvent("thought_chunk",
            u.Payload["content"]?["text"]?.GetValue<string>() ?? ""),
        "tool_call" => new AgentEvent("tool_call",
            u.Payload["title"]?.GetValue<string>() ?? "tool", u.Payload),
        "tool_call_update" => new AgentEvent("tool_update",
            u.Payload["status"]?.GetValue<string>() ?? "", u.Payload),
        "plan" => new AgentEvent("plan", "", u.Payload),
        _ => new AgentEvent(u.Kind, "", u.Payload)
    };

    public void Dispose() => _client?.Dispose();
}

// ------------------------------------------------------------ session model

public sealed record ChatEntry(DateTimeOffset Time, string Role, string Text, string Kind);

public sealed record PermissionOptionDto(string OptionId, string Name, string Kind);

public sealed record PendingPermission(
    string RequestKey, string ToolTitle, string ToolKind,
    IReadOnlyList<PermissionOptionDto> Options);

public sealed class HelmSession : IDisposable
{
    private readonly object _lock = new();
    private readonly List<ChatEntry> _transcript = [];
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string?>> _permissionWaits = new();
    private readonly ConcurrentDictionary<Guid, Channel<SessionEventDto>> _subscribers = new();
    private System.Text.StringBuilder _streamingBuffer = new();

    public required string Id { get; init; }
    public required string AgentId { get; init; }
    public required string Cwd { get; init; }
    public required IAgentAdapter Adapter { get; init; }
    public string? Model { get; init; }
    public string Title { get; set; } = "New session";
    /// <summary>Quick chat: no repository, runs in a scratch directory on the Bridge.</summary>
    public bool IsChat { get; init; }
    public string Policy { get; private set; } = PermissionPolicies.Ask;
    public string Status { get; private set; } = "idle";   // idle | running | error
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActivity { get; private set; } = DateTimeOffset.UtcNow;
    public PendingPermission? Pending { get; private set; }

    public IReadOnlyList<ChatEntry> TranscriptSnapshot()
    {
        lock (_lock) return _transcript.ToList();
    }

    // ------------------------------------------------------------ lifecycle

    public void WireAdapter()
    {
        Adapter.OnEvent += HandleAgentEvent;
        Adapter.PermissionHandler = HandlePermissionAsync;
    }

    public async Task RunPromptAsync(string text, ILogger logger,
        IReadOnlyList<PromptAttachment>? attachments = null)
    {
        var display = attachments is { Count: > 0 }
            ? $"{text}\n[attached: {string.Join(", ", attachments.Select(a => a.Name))}]"
            : text;
        Append(new ChatEntry(DateTimeOffset.UtcNow, "user", display, "message"));
        SetStatus("running");
        try
        {
            var stopReason = await Adapter.PromptAsync(text, attachments, CancellationToken.None);
            FlushStreamingBuffer();
            Publish(new SessionEventDto("turn_end", stopReason, null));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Prompt failed in session {Id}", Id);
            FlushStreamingBuffer();
            Append(new ChatEntry(DateTimeOffset.UtcNow, "system", $"Agent error: {ex.Message}", "error"));
        }
        finally
        {
            SetStatus("idle");
        }
    }

    private void HandleAgentEvent(AgentEvent e)
    {
        LastActivity = DateTimeOffset.UtcNow;
        switch (e.Kind)
        {
            case "assistant_chunk":
                lock (_lock) _streamingBuffer.Append(e.Text);
                Publish(new SessionEventDto("chunk", e.Text, null));
                break;
            case "tool_call":
                FlushStreamingBuffer();
                Append(new ChatEntry(DateTimeOffset.UtcNow, "tool", e.Text, "tool_call"));
                break;
            case "tool_update":
                Publish(new SessionEventDto("tool_update", e.Text, null));
                break;
            case "thought_chunk":
                break; // M0: not persisted; forwarded live only
            default:
                Publish(new SessionEventDto(e.Kind, e.Text, null));
                break;
        }
    }

    /// <summary>Changes the permission policy. Always audited in the transcript.</summary>
    public bool SetPolicy(string policy)
    {
        if (!PermissionPolicies.IsValid(policy) || policy == Policy) return false;
        Policy = policy;
        Append(new ChatEntry(DateTimeOffset.UtcNow, "system",
            $"Permission policy changed to '{policy}'", "policy"));
        Publish(new SessionEventDto("policy", policy, null));
        return true;
    }

    private async Task<string?> HandlePermissionAsync(PermissionAsk ask)
    {
        // Policy first: an auto decision never surfaces a pending prompt, but
        // ALWAYS lands in the transcript — auditability is non-negotiable.
        var decision = PolicyEngine.Decide(ask, Policy);
        if (decision.IsAuto)
        {
            Append(new ChatEntry(DateTimeOffset.UtcNow, "system",
                $"Permission {decision.Reason}", "permission_auto"));
            return decision.OptionId;
        }

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _permissionWaits[ask.RequestKey] = tcs;
        Pending = new PendingPermission(ask.RequestKey, ask.ToolTitle, ask.ToolKind,
            ask.Options.Select(o => new PermissionOptionDto(o.OptionId, o.Name, o.Kind)).ToList());
        Append(new ChatEntry(DateTimeOffset.UtcNow, "system",
            $"Permission requested: {ask.ToolTitle} ({ask.ToolKind})", "permission"));
        Publish(new SessionEventDto("permission", ask.ToolTitle, Pending));

        var chosen = await tcs.Task;   // resolved by the API endpoint (user's click)
        Pending = null;
        Append(new ChatEntry(DateTimeOffset.UtcNow, "system",
            chosen is null ? "Permission denied by user" : "Permission granted by user",
            "permission_result"));
        Publish(new SessionEventDto("permission_resolved", chosen ?? "denied", null));
        return chosen;
    }

    /// <summary>Renames the session; the UI hears about it via the "title" event.</summary>
    public bool SetTitle(string title)
    {
        title = (title ?? "").Trim();
        if (title.Length is 0 or > 120) return false;
        Title = title;
        Publish(new SessionEventDto("title", title, null));
        return true;
    }

    /// <summary>Audit entry for user actions outside the chat flow (git accept/reject etc.).</summary>
    public void Audit(string text, string kind = "audit") =>
        Append(new ChatEntry(DateTimeOffset.UtcNow, "system", text, kind));

    public bool ResolvePermission(string requestKey, string? optionId)
    {
        if (!_permissionWaits.TryRemove(requestKey, out var tcs)) return false;
        tcs.TrySetResult(optionId);
        return true;
    }

    // --------------------------------------------------------- event fan-out

    public ChannelReader<SessionEventDto> Subscribe(out Guid token)
    {
        token = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<SessionEventDto>();
        _subscribers[token] = channel;
        return channel.Reader;
    }

    public void Unsubscribe(Guid token)
    {
        if (_subscribers.TryRemove(token, out var channel))
            channel.Writer.TryComplete();
    }

    private void Publish(SessionEventDto e)
    {
        foreach (var (_, channel) in _subscribers)
            channel.Writer.TryWrite(e);
    }

    private void Append(ChatEntry entry)
    {
        lock (_lock) _transcript.Add(entry);
        Publish(new SessionEventDto("entry", entry.Text, entry));
    }

    private void FlushStreamingBuffer()
    {
        string text;
        lock (_lock)
        {
            if (_streamingBuffer.Length == 0) return;
            text = _streamingBuffer.ToString();
            _streamingBuffer = new System.Text.StringBuilder();
        }
        Append(new ChatEntry(DateTimeOffset.UtcNow, "assistant", text, "message"));
    }

    private void SetStatus(string status)
    {
        Status = status;
        Publish(new SessionEventDto("status", status, null));
    }

    public void Dispose()
    {
        foreach (var (_, tcs) in _permissionWaits) tcs.TrySetResult(null);
        foreach (var (_, channel) in _subscribers) channel.Writer.TryComplete();
        Adapter.Dispose();
    }
}

/// <summary>Wire shape of one live event (SSE payload).</summary>
public sealed record SessionEventDto(string Kind, string Text, object? Data);

// ----------------------------------------------------------- session manager

public sealed class SessionManager(AgentCatalog catalog, ILoggerFactory loggerFactory) : IDisposable
{
    private readonly ConcurrentDictionary<string, HelmSession> _sessions = new();

    public IReadOnlyCollection<HelmSession> All => _sessions.Values.ToList();

    public HelmSession? Get(string id) => _sessions.GetValueOrDefault(id);

    public async Task<HelmSession> CreateAsync(
        string agentId, string cwd, string? title, CancellationToken ct,
        string? policy = null, string? resumeNativeSessionId = null, string? model = null, bool isChat = false)
    {
        var spec = catalog.Find(agentId)
            ?? throw new ArgumentException($"Unknown agent '{agentId}'. Configure it under AgentHelm:Agents.");
        var id = Guid.NewGuid().ToString("N")[..12];
        if (isChat && string.IsNullOrWhiteSpace(cwd))
            cwd = CreateChatDirectory(id);
        if (!Directory.Exists(cwd))
            throw new ArgumentException(
                $"Working directory does not exist: {cwd}. " +
                "The path must be accessible on the Bridge server (the machine running AgentHelm.Bridge), not the browser's machine. " +
                "Running Bridge in Docker? Add a volumes: entry in docker-compose (e.g. - C:\\Repos:/repos) and use the container path (/repos/myproject).");

        var logger = loggerFactory.CreateLogger($"agent.{agentId}");
        IAgentAdapter adapter = BuildAdapter(spec, cwd, logger, resumeNativeSessionId, model);
        var session = new HelmSession
        {
            Id = id,
            AgentId = agentId,
            Cwd = cwd,
            Adapter = adapter,
            Model = model,
            IsChat = isChat,
            Title = string.IsNullOrWhiteSpace(title)
                ? isChat ? "New chat" : $"{spec.Name} · {Path.GetFileName(cwd)}"
                : title
        };
        if (PermissionPolicies.IsValid(policy)) session.SetPolicy(policy!);
        session.WireAdapter();
        await adapter.StartAsync(ct);

        _sessions[session.Id] = session;
        return session;
    }

    /// <summary>Scratch working directory for a quick chat, created on the Bridge's machine.</summary>
    public static string CreateChatDirectory(string sessionId)
    {
        var dir = Path.Combine(Path.GetTempPath(), "AgentHelm", "chats", sessionId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Adapter factory keyed by AgentSpec.Type. This is the whole activation
    /// story for the SDK adapter: with the COPILOT_SDK symbol and the preview
    /// package, "copilot-sdk" agents just work; without them the error tells
    /// you exactly what is missing instead of failing somewhere deep.
    /// </summary>
    private static IAgentAdapter BuildAdapter(AgentSpec spec, string cwd, ILogger logger,
        string? resumeNativeSessionId, string? model)
    {
        var type = string.IsNullOrWhiteSpace(spec.Type) ? "acp" : spec.Type.ToLowerInvariant();
        switch (type)
        {
            case "acp":
                return new AcpAdapter(spec, cwd, logger, resumeNativeSessionId, model);
            case "copilot-sdk":
#if COPILOT_SDK
                return new global::AgentHelm.Bridge.Agents.CopilotSdk.CopilotSdkAdapter(
                    spec, cwd, logger, model, resumeNativeSessionId);
#else
                throw new ArgumentException(
                    "Agent type 'copilot-sdk' requires building with the COPILOT_SDK symbol and the " +
                    "GitHub Copilot SDK package. See Agents/CopilotSdk/CopilotSdkAdapter.cs for the steps.");
#endif
            default:
                throw new ArgumentException(
                    $"Unknown agent type '{spec.Type}' for agent '{spec.Id}'. Supported: acp, copilot-sdk.");
        }
    }

    /// <summary>
    /// Handoff context: a compact, user-reviewable summary of the conversation
    /// so far, meant to be PREFILLED into the composer of the new session —
    /// never auto-sent. The user sees exactly what the next agent will read.
    /// Trimmed from the FRONT when over budget: recency wins.
    /// </summary>
    public static string BuildHandoffContext(HelmSession session, int maxChars = 4000)
    {
        var lines = session.TranscriptSnapshot()
            .Where(e => e.Kind is "message" or "tool_call")
            .Select(e => $"{e.Role}: {e.Text}")
            .ToList();

        var body = string.Join("\n", lines);
        if (body.Length > maxChars)
            body = "[…earlier conversation trimmed…]\n" + body[^maxChars..];

        return
            $"[Handoff] You are taking over an ongoing coding session previously handled by agent " +
            $"'{session.AgentId}' in {session.Cwd}.\nConversation so far (most recent last):\n{body}\n" +
            "Please continue from here.";
    }

    public bool Remove(string id)
    {
        if (!_sessions.TryRemove(id, out var session)) return false;
        session.Dispose();
        return true;
    }

    public void Dispose()
    {
        foreach (var (_, s) in _sessions) s.Dispose();
        _sessions.Clear();
    }
}
