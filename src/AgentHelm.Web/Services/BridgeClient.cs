using System.Net.Http.Json;
using System.Text.Json;

namespace AgentHelm.Web.Services;

public sealed record AgentDto(string Id, string Name);

public sealed record SessionSummaryDto(
    string Id, string AgentId, string Cwd, string Title, string Status, string Policy,
    DateTimeOffset CreatedAt, DateTimeOffset LastActivity, bool HasPendingPermission);

public sealed record ChatEntryDto(DateTimeOffset Time, string Role, string Text, string Kind);

public sealed record PermissionOptionDto(string OptionId, string Name, string Kind);

public sealed record PendingPermissionDto(
    string RequestKey, string ToolTitle, string ToolKind, List<PermissionOptionDto> Options);

public sealed record SessionDetailDto(
    string Id, string AgentId, string Cwd, string Title, string Status, string Policy, string? Model,
    DateTimeOffset CreatedAt, DateTimeOffset LastActivity, AgentCapsDto? Caps,
    PendingPermissionDto? Pending, List<ChatEntryDto> Transcript);

public sealed record AttachmentDto(string Kind, string Name, string MimeType, string Data);

public sealed record AgentCapsDto(bool LoadSession, bool Image, bool Audio, bool EmbeddedContext);

public sealed record ScopeMatchDto(
    string Id, string? Title, string? Model, double? Score, string? Grade, double? Confidence,
    DateTimeOffset? LastActivity);
public sealed record ScopeResultDto(bool Available, List<ScopeMatchDto> Matches);

public sealed record HandoffResultDto(SessionSummaryDto Session, string Context);

public sealed record GitFileChangeDto(string Path, string Status, bool Untracked);
public sealed record GitChangesDto(bool IsRepo, List<GitFileChangeDto> Files);
public sealed record GitFileDiffDto(string Path, string Status, string DiffText, int Additions, int Deletions);

public sealed record ArchivedSessionDto(
    string Id, string AgentId, string Cwd, string Title,
    DateTimeOffset CreatedAt, DateTimeOffset LastActivity,
    List<ChatEntryDto> Transcript, string? NativeSessionId);

public sealed record SessionEventDto(string Kind, string Text, JsonElement? Data);

public sealed record DirListDto(string Path, string? Parent, string[] Dirs);

public sealed record ProviderInfoDto(
    string Id, string Name, string? Account, string Status,
    string? LoginCommand, string? LogoutCommand);

public sealed record ProviderModelDto(string Id, string Name, bool IsDefault);

public sealed record ProviderLoginEventDto(string Kind, string? Text, int? ExitCode);

public sealed class BridgeClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public BridgeClient(IConfiguration configuration)
    {
        // Aspire service discovery first, explicit config second, default third.
        var baseUrl = configuration["services:bridge:http:0"];
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = configuration["Bridge:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = "http://127.0.0.1:5199";
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var token = configuration["Bridge:ApiToken"];
        if (!string.IsNullOrEmpty(token))
            _http.DefaultRequestHeaders.Add("x-helm-token", token);
    }

    public async Task<List<AgentDto>> GetAgentsAsync(CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<List<AgentDto>>("/api/agents", Json, ct) ?? []; }
        catch { return []; }
    }

    public async Task<List<SessionSummaryDto>> GetSessionsAsync(CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<List<SessionSummaryDto>>("/api/sessions", Json, ct) ?? []; }
        catch { return []; }
    }

    public Task<SessionDetailDto?> GetSessionAsync(string id, CancellationToken ct = default)
    {
        try { return _http.GetFromJsonAsync<SessionDetailDto>($"/api/sessions/{id}", Json, ct); }
        catch { return Task.FromResult<SessionDetailDto?>(null); }
    }

    public async Task<(SessionSummaryDto? Session, string? Error)> CreateSessionAsync(
        string agentId, string cwd, string? title, string? model = null, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/sessions", new { agentId, cwd, title, model }, Json, ct);
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<SessionSummaryDto>(Json, ct), null);
        var body = await response.Content.ReadAsStringAsync(ct);
        return (null, string.IsNullOrWhiteSpace(body) ? response.StatusCode.ToString() : body);
    }

    public Task SetTitleAsync(string id, string title, CancellationToken ct = default) =>
        _http.PostAsJsonAsync($"/api/sessions/{id}/title", new { title }, Json, ct);

    public Task SetPolicyAsync(string id, string policy, CancellationToken ct = default) =>
        _http.PostAsJsonAsync($"/api/sessions/{id}/policy", new { policy }, Json, ct);

    public async Task<List<ArchivedSessionDto>> GetHistoryAsync(CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<List<ArchivedSessionDto>>("/api/history", Json, ct) ?? []; }
        catch { return []; }
    }

    public async Task<(SessionSummaryDto? Session, string? Error)> ResumeAsync(
        string archivedId, string? title, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync($"/api/history/{archivedId}/resume", new { title }, Json, ct);
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<SessionSummaryDto>(Json, ct), null);
        var body = await response.Content.ReadAsStringAsync(ct);
        return (null, string.IsNullOrWhiteSpace(body) ? response.StatusCode.ToString() : body);
    }

    /// <summary>Sends a prompt. Null when the Bridge accepted it, otherwise why it did not.</summary>
    public async Task<string?> PromptAsync(string id, string text, List<AttachmentDto>? attachments = null, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync($"/api/sessions/{id}/prompt", new { text, attachments }, Json, ct);
            if (response.IsSuccessStatusCode) return null;
            if (response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge)
                return "The attachments are too large for one prompt. Remove some and send again.";
            var body = await response.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(body) ? $"The Bridge refused the prompt ({(int)response.StatusCode})." : body;
        }
        catch (HttpRequestException ex)
        {
            return $"Could not reach the Bridge: {ex.Message}";
        }
    }

    // ------------------------------------------------------------- M2: git
    public async Task<GitChangesDto> GetGitChangesAsync(string id, CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<GitChangesDto>($"/api/sessions/{id}/git/changes", Json, ct) ?? new(false, []); }
        catch { return new(false, []); }
    }

    public async Task<GitFileDiffDto?> GetGitDiffAsync(string id, string path, CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<GitFileDiffDto>($"/api/sessions/{id}/git/diff?path={Uri.EscapeDataString(path)}", Json, ct); }
        catch { return null; }
    }

    public Task GitAcceptAsync(string id, string path, CancellationToken ct = default) =>
        _http.PostAsJsonAsync($"/api/sessions/{id}/git/accept", new { path }, Json, ct);

    public Task GitRejectAsync(string id, string path, CancellationToken ct = default) =>
        _http.PostAsJsonAsync($"/api/sessions/{id}/git/reject", new { path }, Json, ct);

    // -------------------------------------------------------- M2: terminal
    public async Task<bool> StartTerminalAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsync($"/api/sessions/{id}/terminal/start", null, ct);
            var result = await response.Content.ReadFromJsonAsync<TerminalStartDto>(Json, ct);
            return result?.Pty ?? false;
        }
        catch { return false; }
    }

    private sealed record TerminalStartDto(bool Pty);

    public async Task<(HandoffResultDto? Result, string? Error)> HandoffAsync(
        string id, string agentId, string? title = null, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync($"/api/sessions/{id}/handoff", new { agentId, title }, Json, ct);
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<HandoffResultDto>(Json, ct), null);
        var body = await response.Content.ReadAsStringAsync(ct);
        return (null, string.IsNullOrWhiteSpace(body) ? response.StatusCode.ToString() : body);
    }

    public async Task<ScopeResultDto> GetScopeAsync(string id, CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<ScopeResultDto>($"/api/sessions/{id}/scope", Json, ct) ?? new(false, []); }
        catch { return new(false, []); }
    }

    public Task TerminalInputAsync(string id, string text, CancellationToken ct = default) =>
        _http.PostAsJsonAsync($"/api/sessions/{id}/terminal/input", new { text }, Json, ct);

    public async Task<string> GetTerminalBufferAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<TerminalBufferDto>($"/api/sessions/{id}/terminal/buffer", Json, ct);
            return result?.Text ?? "";
        }
        catch { return ""; }
    }

    private sealed record TerminalBufferDto(string Text);

    public Task SubscribeTerminalAsync(string id, Func<SessionEventDto, Task> onEvent, CancellationToken ct) =>
        ReadSseAsync($"/api/sessions/{id}/terminal/stream", onEvent, ct);

    public Task ResolvePermissionAsync(string id, string requestKey, bool allow, string? optionId, CancellationToken ct = default) =>
        _http.PostAsJsonAsync($"/api/sessions/{id}/permission", new { requestKey, allow, optionId }, Json, ct);

    public Task CancelAsync(string id, CancellationToken ct = default) =>
        _http.PostAsync($"/api/sessions/{id}/cancel", null, ct);

    public Task DeleteAsync(string id, CancellationToken ct = default) =>
        _http.DeleteAsync($"/api/sessions/{id}", ct);

    /// <summary>
    /// Subscribes to the session's SSE stream; invokes the callback per event
    /// until cancelled. Runs on the Blazor Server side — the browser never
    /// talks to the Bridge directly.
    /// </summary>
    public Task SubscribeAsync(string id, Func<SessionEventDto, Task> onEvent, CancellationToken ct) =>
        ReadSseAsync($"/api/sessions/{id}/stream", onEvent, ct);

    // ------------------------------------------------- filesystem browser
    public async Task<DirListDto> GetDirectoriesAsync(string? path, CancellationToken ct = default)
    {
        try
        {
            var url = string.IsNullOrWhiteSpace(path)
                ? "/api/fs/dirs"
                : $"/api/fs/dirs?path={Uri.EscapeDataString(path)}";
            return await _http.GetFromJsonAsync<DirListDto>(url, Json, ct) ?? new("", null, []);
        }
        catch { return new("", null, []); }
    }

    // ------------------------------------------------- runtime config
    public async Task<string> GetScopeUrlAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<ScopeConfigDto>("/api/config", Json, ct);
            return result?.ScopeUrl ?? "http://localhost:4318";
        }
        catch { return "http://localhost:4318"; }
    }

    public Task SetScopeUrlAsync(string url, CancellationToken ct = default) =>
        _http.PostAsJsonAsync("/api/config/scope-url", new { url }, Json, ct);

    private sealed record ScopeConfigDto(string ScopeUrl);

    // -------------------------------------------- provider account / login
    public async Task<List<ProviderInfoDto>> GetProvidersAsync(CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<List<ProviderInfoDto>>("/api/providers", Json, ct) ?? []; }
        catch { return []; }
    }

    public async Task<List<ProviderModelDto>> GetProviderModelsAsync(string id, CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<List<ProviderModelDto>>($"/api/providers/{id}/models", Json, ct) ?? []; }
        catch { return []; }
    }

    public async Task<bool> StartProviderLoginAsync(string id, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/providers/{id}/login/start", null, ct);
        return resp.IsSuccessStatusCode;
    }

    public Task SubscribeProviderLoginAsync(string id, Func<ProviderLoginEventDto, Task> onEvent, CancellationToken ct) =>
        ReadProviderSseAsync($"/api/providers/{id}/login/stream", onEvent, ct);

    public async Task<bool> LogoutProviderAsync(string id, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/providers/{id}/logout", null, ct);
        return resp.IsSuccessStatusCode;
    }

    private async Task ReadProviderSseAsync(string url, Func<ProviderLoginEventDto, Task> onEvent, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (!ct.IsCancellationRequested && await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            ProviderLoginEventDto? evt = null;
            try { evt = JsonSerializer.Deserialize<ProviderLoginEventDto>(line["data: ".Length..], Json); }
            catch (JsonException) { }
            if (evt is not null) await onEvent(evt);
        }
    }

    private async Task ReadSseAsync(string url, Func<SessionEventDto, Task> onEvent, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (!ct.IsCancellationRequested && await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            SessionEventDto? evt = null;
            try { evt = JsonSerializer.Deserialize<SessionEventDto>(line["data: ".Length..], Json); }
            catch (JsonException) { /* tolerate malformed frames */ }
            if (evt is not null) await onEvent(evt);
        }
    }
}
