using System.Diagnostics;
using System.Text.Json;
using AgentHelm.Bridge.Providers;
using AgentHelm.Bridge.Sessions;

namespace AgentHelm.Bridge;

/// <summary>
/// The Bridge API composed into a host. Program.cs runs it on its own; AgentHelm.App
/// runs it in the same process as the UI.
/// </summary>
public static class AgentHelmBridgeExtensions
{
    public static WebApplicationBuilder AddAgentHelmBridge(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<AgentCatalog>();
        builder.Services.AddSingleton<SessionManager>();
        builder.Services.AddSingleton<AgentHelm.Bridge.Workbench.IGitRunner, AgentHelm.Bridge.Workbench.ProcessGitRunner>();
        builder.Services.AddSingleton<AgentHelm.Bridge.Workbench.GitService>();
        builder.Services.AddSingleton<AgentHelm.Bridge.Workbench.TerminalManager>();
        builder.Services.AddSingleton<AgentHelm.Bridge.Integrations.ScopeClient>();
        builder.Services.AddSingleton<ProviderAccountService>();

        // Persistence (optional — degrades to memory-only when no connection string).
        var helmDbConnection = builder.Configuration.GetConnectionString("helmdb");
        if (!string.IsNullOrEmpty(helmDbConnection))
        {
            builder.Services.AddSingleton(sp => new AgentHelm.Bridge.Persistence.SessionRepository(
                helmDbConnection, sp.GetRequiredService<ILogger<AgentHelm.Bridge.Persistence.SessionRepository>>()));
            builder.Services.AddSingleton<AgentHelm.Bridge.Persistence.PersistenceWriter>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<AgentHelm.Bridge.Persistence.PersistenceWriter>());
        }

        return builder;
    }

    public static WebApplication MapAgentHelmBridge(this WebApplication app)
    {
        var sessions = app.Services.GetRequiredService<SessionManager>();
        var catalog = app.Services.GetRequiredService<AgentCatalog>();
        var logger = app.Services.GetRequiredService<ILogger<Program>>();

        // Optional shared token (AgentHelm:ApiToken). Even on loopback this matters:
        // any web page in the user's browser can fire requests at localhost.
        var apiToken = app.Configuration["AgentHelm:ApiToken"];
        if (!string.IsNullOrEmpty(apiToken))
        {
            app.Use(async (ctx, next) =>
            {
                if (ctx.Request.Headers["x-helm-token"].FirstOrDefault() != apiToken)
                {
                    ctx.Response.StatusCode = 401;
                    return;
                }
                await next();
            });
        }

        var persistence = app.Services.GetService<AgentHelm.Bridge.Persistence.PersistenceWriter>();
        var git = app.Services.GetRequiredService<AgentHelm.Bridge.Workbench.GitService>();
        var terminals = app.Services.GetRequiredService<AgentHelm.Bridge.Workbench.TerminalManager>();
        var scope = app.Services.GetRequiredService<AgentHelm.Bridge.Integrations.ScopeClient>();
        var providers = app.Services.GetRequiredService<ProviderAccountService>();
        var api = app.MapGroup("/api");

        api.MapGet("/agents", () => Results.Ok(catalog.All.Select(a => new { a.Id, a.Name })));

        api.MapGet("/sessions", () => Results.Ok(sessions.All
            .OrderByDescending(s => s.LastActivity)
            .Select(SessionSummary)));

        api.MapPost("/sessions", async (CreateSessionRequest req, CancellationToken ct) =>
        {
            try
            {
                var session = await sessions.CreateAsync(req.AgentId, req.Cwd ?? "", req.Title, ct, req.Policy,
                    model: req.Model, isChat: req.IsChat);
                logger.LogInformation("Session {Id} started: agent={Agent} cwd={Cwd}", session.Id, req.AgentId, req.Cwd);
                return Results.Ok(SessionSummary(session));
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to start session for agent {Agent}", req.AgentId);
                return Results.Problem($"Could not start agent '{req.AgentId}': {ex.Message}");
            }
        });

        api.MapGet("/sessions/{id}", (string id) =>
            sessions.Get(id) is { } s
                ? Results.Ok(new
                {
                    s.Id, s.AgentId, s.Cwd, s.Title, s.Status, s.Policy, s.Model, s.IsChat, s.CreatedAt, s.LastActivity,
                    Caps = s.Adapter.Capabilities,
                    Pending = s.Pending,
                    Transcript = s.TranscriptSnapshot()
                })
                : Results.NotFound());

        api.MapPost("/sessions/{id}/prompt", (string id, PromptRequest req) =>
        {
            if (sessions.Get(id) is not { } session) return Results.NotFound();
            if (session.Status == "running") return Results.Conflict(new { error = "Session is already running a turn." });
            // Attachment size cap: 8 MB total base64 — a Bridge on loopback is not a file server.
            var totalSize = req.Attachments?.Sum(a => (long)a.Data.Length) ?? 0;
            if (totalSize > 8_000_000)
                return Results.StatusCode(413);
            _ = Task.Run(async () =>
            {
                await session.RunPromptAsync(req.Text, logger, req.Attachments);
                persistence?.MarkDirty(session);
            });
            return Results.Accepted();
        });

        // Archived transcripts from previous Bridge runs (read-only history).
        api.MapGet("/history", async (CancellationToken ct) =>
        {
            if (app.Services.GetService<AgentHelm.Bridge.Persistence.SessionRepository>() is not { } repo)
                return Results.Ok(Array.Empty<object>());
            try { return Results.Ok(await repo.LoadAllAsync(ct)); }
            catch { return Results.Ok(Array.Empty<object>()); }
        });

        api.MapPost("/sessions/{id}/permission", (string id, PermissionDecision req) =>
        {
            if (sessions.Get(id) is not { } session) return Results.NotFound();
            if (!session.ResolvePermission(req.RequestKey, req.Allow ? req.OptionId : null))
                return Results.NotFound(new { error = "No such pending permission request." });
            persistence?.MarkDirty(session);
            return Results.Ok();
        });

        api.MapPost("/sessions/{id}/title", (string id, TitleRequest req) =>
        {
            if (sessions.Get(id) is not { } session) return Results.NotFound();
            if (!session.SetTitle(req.Title))
                return Results.BadRequest(new { error = "Title must be 1-120 characters." });
            persistence?.MarkDirty(session);
            return Results.Ok(new { title = session.Title });
        });

        api.MapPost("/sessions/{id}/policy", (string id, PolicyRequest req) =>
        {
            if (sessions.Get(id) is not { } session) return Results.NotFound();
            if (!PermissionPolicies.IsValid(req.Policy))
                return Results.BadRequest(new { error = $"Unknown policy. Valid: {string.Join(", ", PermissionPolicies.All)}" });
            session.SetPolicy(req.Policy);
            persistence?.MarkDirty(session);
            return Results.Ok(new { policy = session.Policy });
        });

        // Resume an archived session (CLI <-> GUI continuity via ACP session/load).
        api.MapPost("/history/{id}/resume", async (string id, ResumeRequest req, CancellationToken ct) =>
        {
            if (app.Services.GetService<AgentHelm.Bridge.Persistence.SessionRepository>() is not { } repo)
                return Results.BadRequest(new { error = "Persistence is not configured — nothing to resume from." });
            if (await repo.LoadAsync(id, ct) is not { } archived)
                return Results.NotFound(new { error = "No such archived session." });
            if (string.IsNullOrEmpty(archived.NativeSessionId))
                return Results.BadRequest(new { error = "This archived session predates resume support (no agent-side session id)." });
            try
            {
                var session = await sessions.CreateAsync(
                    archived.AgentId, archived.Cwd, req.Title ?? $"{archived.Title} (resumed)",
                    ct, resumeNativeSessionId: archived.NativeSessionId, isChat: archived.IsChat);
                return Results.Ok(SessionSummary(session));
            }
            catch (Exception ex)
            {
                return Results.Problem($"Resume failed (the agent may not support session/load): {ex.Message}");
            }
        });

        api.MapPost("/sessions/{id}/cancel", async (string id, CancellationToken ct) =>
        {
            if (sessions.Get(id) is not { } session) return Results.NotFound();
            await session.Adapter.CancelAsync(ct);
            return Results.Ok();
        });

        api.MapDelete("/sessions/{id}", async (string id, CancellationToken ct) =>
        {
            if (!sessions.Remove(id)) return Results.NotFound();
            terminals.Remove(id);
            if (app.Services.GetService<AgentHelm.Bridge.Persistence.SessionRepository>() is { } repo)
            {
                try { await repo.DeleteAsync(id, ct); } catch { /* history cleanup is best-effort */ }
            }
            return Results.Ok();
        });

        // Live event stream (SSE) — the UI's realtime feed.
        api.MapGet("/sessions/{id}/stream", async (string id, HttpContext ctx, CancellationToken ct) =>
        {
            if (sessions.Get(id) is not { } session) { ctx.Response.StatusCode = 404; return; }

            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            var reader = session.Subscribe(out var token);
            try
            {
                await foreach (var e in reader.ReadAllAsync(ct))
                {
                    await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(e)}\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { }
            finally { session.Unsubscribe(token); }
        });

        // ------------------------------------------------------------ M2: git diff
        api.MapGet("/sessions/{id}/git/changes", async (string id, CancellationToken ct) =>
        {
            if (sessions.Get(id) is not { } session) return Results.NotFound();
            if (!await git.IsRepoAsync(session.Cwd, ct))
                return Results.Ok(new { isRepo = false, files = Array.Empty<object>() });
            try { return Results.Ok(new { isRepo = true, files = await git.ChangesAsync(session.Cwd, ct) }); }
            catch (Exception ex) { return Results.Problem($"git status failed: {ex.Message}"); }
        });

        api.MapGet("/sessions/{id}/git/diff", async (string id, string path, CancellationToken ct) =>
        {
            if (sessions.Get(id) is not { } session) return Results.NotFound();
            try { return Results.Ok(await git.DiffAsync(session.Cwd, path, ct)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (Exception ex) { return Results.Problem($"git diff failed: {ex.Message}"); }
        });

        api.MapPost("/sessions/{id}/git/accept", async (string id, GitPathRequest req, CancellationToken ct) =>
        {
            if (sessions.Get(id) is not { } session) return Results.NotFound();
            try
            {
                await git.AcceptAsync(session.Cwd, req.Path, ct);
                session.Audit($"Accepted (staged) changes: {req.Path}", "git");
                persistence?.MarkDirty(session);
                return Results.Ok();
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        api.MapPost("/sessions/{id}/git/reject", async (string id, GitPathRequest req, CancellationToken ct) =>
        {
            if (sessions.Get(id) is not { } session) return Results.NotFound();
            try
            {
                await git.RejectAsync(session.Cwd, req.Path, ct);
                session.Audit($"Rejected (reverted) changes: {req.Path}", "git");
                persistence?.MarkDirty(session);
                return Results.Ok();
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        // ----------------------------------------------------------- M2: terminal
        api.MapGet("/sessions/{id}/terminals", (string id) =>
            sessions.Get(id) is null
                ? Results.NotFound()
                : Results.Ok(terminals.List(id).Select(t => new { id = t.Id, pty = t.IsPty, exited = t.HasExited })));

        api.MapPost("/sessions/{id}/terminals", (string id) =>
        {
            if (sessions.Get(id) is not { } session) return Results.NotFound();
            var terminal = terminals.Create(id, session.Cwd);
            return Results.Ok(new { id = terminal.Id, pty = terminal.IsPty, exited = false });
        });

        api.MapDelete("/sessions/{id}/terminals/{terminalId}", (string id, string terminalId) =>
            terminals.Close(id, terminalId) ? Results.Ok() : Results.NotFound());

        api.MapPost("/sessions/{id}/terminals/{terminalId}/input", async (string id, string terminalId, TerminalInputRequest req, CancellationToken ct) =>
        {
            if (terminals.Get(id, terminalId) is not { } terminal) return Results.NotFound();
            await terminal.WriteInputAsync(req.Text, ct);
            return Results.Ok();
        });

        api.MapGet("/sessions/{id}/terminals/{terminalId}/buffer", (string id, string terminalId) =>
            terminals.Get(id, terminalId) is { } terminal
                ? Results.Ok(new { text = terminal.BufferSnapshot() })
                : Results.NotFound());

        api.MapGet("/sessions/{id}/terminals/{terminalId}/stream", async (string id, string terminalId, HttpContext ctx, CancellationToken ct) =>
        {
            if (terminals.Get(id, terminalId) is not { } terminal) { ctx.Response.StatusCode = 404; return; }
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            var reader = terminal.Subscribe(out var token);
            try
            {
                await foreach (var e in reader.ReadAllAsync(ct))
                {
                    await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(e)}\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { }
            finally { terminal.Unsubscribe(token); }
        });

        // ------------------------------------------------------- M3: agent handoff
        api.MapPost("/sessions/{id}/handoff", async (string id, HandoffRequest req, CancellationToken ct) =>
        {
            if (sessions.Get(id) is not { } source) return Results.NotFound();
            try
            {
                var created = await sessions.CreateAsync(
                    req.AgentId, source.Cwd, req.Title ?? $"{req.AgentId} · handoff",
                    ct, policy: source.Policy);
                var context = SessionManager.BuildHandoffContext(source);
                source.Audit($"Handoff to '{req.AgentId}' → session {created.Id}", "handoff");
                persistence?.MarkDirty(source);
                // Context is returned for the composer — the USER sends it, never Helm.
                return Results.Ok(new { session = SessionSummary(created), context });
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (Exception ex) { return Results.Problem($"Handoff failed: {ex.Message}"); }
        });

        // ------------------------------------------ M3: CopilotScope quality inline
        api.MapGet("/sessions/{id}/scope", async (string id, CancellationToken ct) =>
        {
            if (sessions.Get(id) is not { } session) return Results.NotFound();
            var recent = await scope.RecentAsync(ct);
            if (recent is null) return Results.Ok(new { available = false, matches = Array.Empty<object>() });
            var matches = AgentHelm.Bridge.Integrations.ScopeClient.Correlate(
                recent, session.CreatedAt, session.LastActivity);
            return Results.Ok(new { available = true, matches });
        });

        api.MapGet("/health", () => Results.Ok(new { status = "ok", sessions = sessions.All.Count }));

        // ------------------------------------------------- filesystem directory browser
        api.MapGet("/fs/dirs", (string? path) =>
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(homeDir) && Directory.Exists(homeDir))
                    path = homeDir;
                else
                    path = "/";
            }

            path = Path.GetFullPath(path);
            if (!Directory.Exists(path))
                return Results.BadRequest(new { error = $"Directory not found: {path}" });

            string[] names;
            try
            {
                names = Directory.GetDirectories(path)
                    .Select(d => Path.GetFileName(d) ?? d)
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (UnauthorizedAccessException) { names = []; }

            var parent = Path.GetDirectoryName(path);
            return Results.Ok(new { path, parent, dirs = names });
        });

        // ---------------------------------------- runtime config (scope URL, etc.)
        api.MapGet("/config", () => Results.Ok(new { scopeUrl = scope.BaseUrl }));
        api.MapPost("/config/scope-url", (ScopeUrlRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.Url))
                return Results.BadRequest(new { error = "URL is required." });
            scope.UpdateBaseUrl(req.Url.Trim());
            return Results.Ok(new { scopeUrl = scope.BaseUrl });
        });

        // ----------------------------------------- provider account / login APIs
        api.MapGet("/providers", () =>
            catalog.All
                .Select(a => providers.GetInfo(a.Id, a.Name))
                .Where(p => p is not null)
                .ToList());

        api.MapGet("/providers/{id}/models", async (string id, CancellationToken ct) =>
        {
            if (catalog.Find(id) is null) return Results.NotFound();
            return Results.Ok(await providers.GetModelsAsync(id, ct));
        });

        api.MapPost("/providers/{id}/login/start", (string id) =>
        {
            if (catalog.Find(id) is not { } agent) return Results.NotFound();
            if (providers.GetInfo(id, agent.Name)?.LoginCommand is not { } cmd)
                return Results.BadRequest(new { error = "No login command configured for this provider." });
            providers.StartLogin(id, cmd);
            return Results.Ok(new { started = true });
        });

        api.MapGet("/providers/{id}/login/stream", async (string id, HttpContext ctx, CancellationToken ct) =>
        {
            if (providers.GetLogin(id) is not { } login) { ctx.Response.StatusCode = 404; return; }
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            await foreach (var line in login.StreamAsync(ct))
            {
                await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { kind = "output", text = line })}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
            }
            if (!ct.IsCancellationRequested)
            {
                await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { kind = "done", exitCode = login.ExitCode })}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
            }
        });

        api.MapPost("/providers/{id}/logout", async (string id, CancellationToken ct) =>
        {
            if (catalog.Find(id) is not { } agent) return Results.NotFound();
            var info = providers.GetInfo(id, agent.Name);
            if (info?.LogoutCommand is not { } cmd)
                return Results.BadRequest(new { error = "No logout command configured for this provider." });

            var parts = cmd.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var psi = new ProcessStartInfo(parts[0], parts.Length > 1 ? parts[1] : "")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            try
            {
                using var proc = Process.Start(psi);
                if (proc is null) return Results.Problem("Failed to start logout process.");
                await proc.WaitForExitAsync(ct);
                providers.ClearLogin(id);
                return proc.ExitCode == 0
                    ? Results.Ok(new { success = true })
                    : Results.Problem($"Logout exited with code {proc.ExitCode}.");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Logout failed: {ex.Message}");
            }
        });

        logger.LogInformation("""
            AgentHelm Bridge started.
              API        : GET/POST /api/sessions · SSE /api/sessions/{{id}}/stream
              Agents     : {Agents}
              Security   : bound to loopback; token {Token}
            """,
            string.Join(", ", catalog.All.Select(a => a.Id)),
            string.IsNullOrEmpty(apiToken) ? "disabled (dev)" : "required");

        return app;
    }

    private static object SessionSummary(HelmSession s) => new
    {
        s.Id, s.AgentId, s.Cwd, s.Title, s.Status, s.Policy, s.IsChat, s.CreatedAt, s.LastActivity,
        HasPendingPermission = s.Pending is not null
    };
}
