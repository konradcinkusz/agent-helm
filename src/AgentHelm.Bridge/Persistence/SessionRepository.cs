using System.Text.Json;
using AgentHelm.Bridge.Sessions;
using Npgsql;

namespace AgentHelm.Bridge.Persistence;

// M0 persistence: transcripts survive Bridge restarts as read-only history.
// Live adapters (child processes) cannot be rehydrated — a restarted Bridge
// shows past conversations but new prompts need a new session. Proven
// CopilotScope pattern: jsonb snapshot + graceful degradation to memory-only
// when Postgres is absent.

public sealed record ArchivedSession(
    string Id, string AgentId, string Cwd, string Title,
    DateTimeOffset CreatedAt, DateTimeOffset LastActivity,
    List<ChatEntry> Transcript)
{
    /// <summary>Agent-side session id — enables resume (nullable: pre-M1 rows).</summary>
    public string? NativeSessionId { get; init; }

    /// <summary>Quick chat flag; absent in older snapshots, which load as ordinary sessions.</summary>
    public bool IsChat { get; init; }
}

public sealed class SessionRepository(string connectionString, ILogger<SessionRepository> logger)
{
    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS helm_sessions (
                id            text PRIMARY KEY,
                agent_id      text NOT NULL,
                title         text NOT NULL,
                snapshot      jsonb NOT NULL,
                last_activity timestamptz NOT NULL
            );
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
        logger.LogInformation("Persistence ready (Postgres).");
    }

    public async Task UpsertAsync(HelmSession session, CancellationToken ct)
    {
        var archived = new ArchivedSession(session.Id, session.AgentId, session.Cwd,
            session.Title, session.CreatedAt, session.LastActivity,
            session.TranscriptSnapshot().ToList())
        { NativeSessionId = session.Adapter.NativeSessionId, IsChat = session.IsChat };

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO helm_sessions (id, agent_id, title, snapshot, last_activity)
            VALUES (@id, @agent, @title, @snapshot::jsonb, @activity)
            ON CONFLICT (id) DO UPDATE SET
                title = EXCLUDED.title,
                snapshot = EXCLUDED.snapshot,
                last_activity = EXCLUDED.last_activity;
            """, conn);
        cmd.Parameters.AddWithValue("id", session.Id);
        cmd.Parameters.AddWithValue("agent", session.AgentId);
        cmd.Parameters.AddWithValue("title", session.Title);
        cmd.Parameters.AddWithValue("snapshot", JsonSerializer.Serialize(archived));
        cmd.Parameters.AddWithValue("activity", session.LastActivity);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<ArchivedSession>> LoadAllAsync(CancellationToken ct)
    {
        var result = new List<ArchivedSession>();
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT snapshot FROM helm_sessions ORDER BY last_activity DESC LIMIT 200;", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            try
            {
                var archived = JsonSerializer.Deserialize<ArchivedSession>(reader.GetString(0));
                if (archived is not null) result.Add(archived);
            }
            catch (JsonException) { /* skip corrupt rows, keep booting */ }
        }
        return result;
    }

    public async Task<ArchivedSession?> LoadAsync(string id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT snapshot FROM helm_sessions WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("id", id);
        var raw = await cmd.ExecuteScalarAsync(ct) as string;
        if (raw is null) return null;
        try { return JsonSerializer.Deserialize<ArchivedSession>(raw); }
        catch (JsonException) { return null; }
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("DELETE FROM helm_sessions WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>Write-behind: batches dirty sessions to Postgres once a second.</summary>
public sealed class PersistenceWriter(SessionRepository repository, ILogger<PersistenceWriter> logger)
    : BackgroundService
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, HelmSession> _dirty = new();

    public void MarkDirty(HelmSession session) => _dirty[session.Id] = session;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await repository.InitializeAsync(ct); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Postgres unavailable — running memory-only. History will not survive restarts.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            foreach (var (id, session) in _dirty)
            {
                _dirty.TryRemove(id, out _);
                try { await repository.UpsertAsync(session, ct); }
                catch (Exception ex) { logger.LogDebug(ex, "Persist failed for {Id}; will retry on next change.", id); }
            }
        }
    }
}
