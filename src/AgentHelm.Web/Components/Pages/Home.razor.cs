using AgentHelm.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Text;
using System.Text.Json;

namespace AgentHelm.Web.Components.Pages;

/// <summary>
/// Logic lives in the code-behind (CopilotScope lesson: the Razor parser and
/// non-trivial C# expressions do not mix well). The page holds one live SSE
/// subscription for the selected session; switching sessions swaps it.
/// </summary>
public partial class Home : IDisposable
{
    [Inject] public IJSRuntime JS { get; set; } = default!;

    private List<AgentDto> _agents = [];
    private List<SessionSummaryDto> _sessions = [];
    private SessionDetailDto? _detail;
    private List<ChatEntryDto> _transcript = [];
    private PendingPermissionDto? _pending;
    private readonly StringBuilder _streamingBuilder = new();
    private string _streaming = "";

    private string? _selectedId;
    private bool _showNewSession;
    private string _newAgentId = "";
    private string _newCwd = "";
    private string? _newTitle;
    private string? _newModel;
    private List<ProviderModelDto> _newAgentModels = [];
    private bool _creating;
    private string? _createError;
    private string _prompt = "";

    // M1: history + resume
    private bool _showHistory;
    private List<ArchivedSessionDto> _history = [];
    private ArchivedSessionDto? _archived;
    private bool _resuming;
    private string? _resumeError;

    // M1: permission policy (YOLO needs explicit confirmation)
    private bool _yoloConfirmPending;
    // Bumped to re-create the policy <select>: after the user picks YOLO and
    // does not confirm, the element still shows "YOLO" while the policy never
    // changed, and Blazor has no value change to render it back.
    private int _policySelectVersion;

    // M2: tabs / attachments / git / terminal
    private string _activeTab = "chat";
    private readonly List<AttachmentDto> _attachments = [];
    private string? _attachError;
    private string? _sendError;
    private bool _gitIsRepo = true;
    private List<GitFileChangeDto> _gitChanges = [];
    private GitFileDiffDto? _diff;
    private string[] _diffLines = [];
    private string? _rejectConfirmPath;
    private string _termInput = "";
    private bool _termNeedsInit;
    private bool _termIsPty;
    private CancellationTokenSource? _termCts;

    // directory browser
    private bool _browseOpen;
    private string _browsePath = "";
    private string? _browseParent;
    private string[] _browseDirs = [];
    private bool _browseLoading;
    private bool _browseShowHidden;
    private string? _browseLocalName;

    // preconfiguration panel
    private bool _showPreconfig;
    private string _preconfigOtelUrl = "";

    // M3: handoff + scope
    private bool _handoffOpen;
    private string _handoffAgentId = "";
    private bool _handingOff;
    private string? _handoffError;
    private bool _scopeOpen;
    private ScopeResultDto? _scope;
    private string? _scopeModel; // model name from the best-matching Scope session
    private bool _editingTitle;
    private string _titleDraft = "";

    private readonly CancellationTokenSource _pageCts = new();
    private CancellationTokenSource? _streamCts;

    protected override async Task OnInitializedAsync()
    {
        _agents = await Bridge.GetAgentsAsync(_pageCts.Token);
        _newAgentId = _agents.FirstOrDefault()?.Id ?? "";
        _sessions = await Bridge.GetSessionsAsync(_pageCts.Token);
        await LoadAgentModelsAsync(_newAgentId);
        _ = PollSessionListAsync();
    }

    private async Task OnNewAgentChangedAsync(ChangeEventArgs e)
    {
        _newAgentId = e.Value?.ToString() ?? "";
        _newModel = null;
        await LoadAgentModelsAsync(_newAgentId);
    }

    private async Task LoadAgentModelsAsync(string agentId)
    {
        _newAgentModels = await Bridge.GetProviderModelsAsync(agentId, _pageCts.Token);
        // Pre-select the default model if any.
        _newModel = _newAgentModels.FirstOrDefault(m => m.IsDefault)?.Id
                    ?? _newAgentModels.FirstOrDefault()?.Id;
    }

    /// <summary>The rail refreshes on a slow poll; the open session is fully live via SSE.</summary>
    private async Task PollSessionListAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(_pageCts.Token))
            {
                if (!_showHistory)
                    _sessions = await Bridge.GetSessionsAsync(_pageCts.Token);
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) { }
    }

    // -------------------------------------------------------------- session

    private void ToggleNewSession()
    {
        _showNewSession = !_showNewSession;
        _createError = null;
    }

    private async Task CreateSessionAsync()
    {
        if (string.IsNullOrWhiteSpace(_newAgentId) || string.IsNullOrWhiteSpace(_newCwd)) return;
        _creating = true;
        _createError = null;
        try
        {
            if (_showPreconfig && !string.IsNullOrWhiteSpace(_preconfigOtelUrl))
                await Bridge.SetScopeUrlAsync(_preconfigOtelUrl.Trim(), _pageCts.Token);

            var (session, error) = await Bridge.CreateSessionAsync(_newAgentId, _newCwd.Trim(), _newTitle,
                string.IsNullOrWhiteSpace(_newModel) ? null : _newModel.Trim(), _pageCts.Token);
            if (session is null)
            {
                _createError = error;
                return;
            }
            _showNewSession = false;
            _newTitle = null;
            _sessions = await Bridge.GetSessionsAsync(_pageCts.Token);
            await SelectSessionAsync(session.Id);
        }
        finally
        {
            _creating = false;
        }
    }

    private async Task SelectSessionAsync(string id)
    {
        _archived = null;
        _showHistory = false;
        if (_yoloConfirmPending) _policySelectVersion++;
        _yoloConfirmPending = false;
        _sendError = null;
        _activeTab = "chat";
        _attachments.Clear();
        _attachError = null;
        _diff = null;
        _diffLines = [];
        _rejectConfirmPath = null;
        _termCts?.Cancel();
        _termCts = null;
        _handoffOpen = false;
        _handoffError = null;
        _scopeOpen = false;
        _scope = null;
        _scopeModel = null;
        _editingTitle = false;
        _selectedId = id;
        _streamCts?.Cancel();
        _streamCts = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token);
        _streamingBuilder.Clear();
        _streaming = "";

        _detail = await Bridge.GetSessionAsync(id, _pageCts.Token);
        _transcript = _detail?.Transcript ?? [];
        _pending = _detail?.Pending;
        await InvokeAsync(StateHasChanged);

        var token = _streamCts.Token;
        _ = Task.Run(async () =>
        {
            try { await Bridge.SubscribeAsync(id, e => OnSessionEventAsync(id, e), token); }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                // Stream dropped (bridge restart, session deleted) — the rail
                // poll will reflect reality; nothing to crash here.
            }
        }, token);
    }

    private async Task OnSessionEventAsync(string sessionId, SessionEventDto e)
    {
        if (sessionId != _selectedId) return;

        switch (e.Kind)
        {
            case "chunk":
                _streamingBuilder.Append(e.Text);
                _streaming = _streamingBuilder.ToString();
                break;

            case "entry":
                _streamingBuilder.Clear();
                _streaming = "";
                if (e.Data is { } entryData)
                {
                    var entry = entryData.Deserialize<ChatEntryDto>(
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    if (entry is not null) _transcript.Add(entry);
                }
                break;

            case "permission":
                if (e.Data is { } permissionData)
                    _pending = permissionData.Deserialize<PendingPermissionDto>(
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));
                break;

            case "permission_resolved":
                _pending = null;
                break;

            case "title" when _detail is not null:
                _detail = _detail with { Title = e.Text };
                break;

            case "policy" when _detail is not null:
                _detail = _detail with { Policy = e.Text };
                break;

            case "status" when _detail is not null:
                _detail = _detail with { Status = e.Text };
                break;
        }

        await InvokeAsync(StateHasChanged);
    }

    // --------------------------------------------------------------- actions

    private async Task SendPromptAsync()
    {
        if (_detail is null || string.IsNullOrWhiteSpace(_prompt)) return;
        var text = _prompt.Trim();
        var attachments = _attachments.Count > 0 ? _attachments.ToList() : null;
        _prompt = "";
        _attachments.Clear();
        _attachError = null;
        _sendError = await Bridge.PromptAsync(_detail.Id, text, attachments, _pageCts.Token);
        if (_sendError is null) return;
        // Not sent: hand the prompt and its attachments back along with the reason.
        _prompt = text;
        if (attachments is not null) _attachments.AddRange(attachments);
    }

    // ----------------------------------------------------------- attachments

    private const long MaxAttachmentBytes = 2 * 1024 * 1024;

    private async Task OnFilesSelectedAsync(InputFileChangeEventArgs e)
    {
        _attachError = null;
        foreach (var file in e.GetMultipleFiles(4))
        {
            if (_attachments.Count >= 4) { _attachError = "Max 4 attachments per prompt."; break; }
            if (file.Size > MaxAttachmentBytes) { _attachError = $"{file.Name}: over 2 MB, skipped."; continue; }

            using var memory = new MemoryStream();
            await file.OpenReadStream(MaxAttachmentBytes).CopyToAsync(memory, _pageCts.Token);
            var bytes = memory.ToArray();

            if (file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                _attachments.Add(new AttachmentDto("image", file.Name, file.ContentType, Convert.ToBase64String(bytes)));
                continue;
            }
            // Text attachment: must actually be text — binary blobs would only
            // poison the agent's context.
            if (Array.IndexOf(bytes, (byte)0) >= 0)
            {
                _attachError = $"{file.Name}: binary file (only images and text are supported), skipped.";
                continue;
            }
            var textContent = Encoding.UTF8.GetString(bytes);
            var mime = string.IsNullOrEmpty(file.ContentType) ? "text/plain" : file.ContentType;
            _attachments.Add(new AttachmentDto("text", file.Name, mime, textContent));
        }
    }

    private void RemoveAttachment(AttachmentDto attachment) => _attachments.Remove(attachment);

    // ------------------------------------------------------------------ tabs

    private async Task SwitchTabAsync(string tab)
    {
        _activeTab = tab;
        if (tab == "changes") await LoadChangesAsync();
        if (tab == "terminal" && _detail is not null)
        {
            _termIsPty = await Bridge.StartTerminalAsync(_detail.Id, _pageCts.Token);
            _termNeedsInit = true;   // JS init must wait for the div to render
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_termNeedsInit || _detail is null) return;
        _termNeedsInit = false;

        await JS.InvokeVoidAsync("helmTerm.init", "term-host");
        var backlog = await Bridge.GetTerminalBufferAsync(_detail.Id, _pageCts.Token);
        if (backlog.Length > 0) await JS.InvokeVoidAsync("helmTerm.write", backlog);

        _termCts?.Cancel();
        _termCts = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token);
        var token = _termCts.Token;
        var sessionId = _detail.Id;
        _ = Task.Run(async () =>
        {
            try
            {
                await Bridge.SubscribeTerminalAsync(sessionId, async e =>
                {
                    if (e.Kind == "out")
                        await InvokeAsync(() => JS.InvokeVoidAsync("helmTerm.write", e.Text).AsTask());
                }, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { /* terminal stream dropped; reopening the tab reattaches */ }
        }, token);
    }

    private async Task OnTerminalKeyAsync(KeyboardEventArgs e)
    {
        if (e.Key != "Enter" || _detail is null || string.IsNullOrWhiteSpace(_termInput)) return;
        var command = _termInput.Trim();
        _termInput = "";
        // A real PTY echoes input itself — local echo would double every line.
        if (!_termIsPty) await JS.InvokeVoidAsync("helmTerm.echoInput", command);
        await Bridge.TerminalInputAsync(_detail.Id, command, _pageCts.Token);
    }

    private async Task InsertOutputIntoPromptAsync()
    {
        if (_detail is null) return;
        var buffer = await Bridge.GetTerminalBufferAsync(_detail.Id, _pageCts.Token);
        if (buffer.Length == 0) return;
        var tail = buffer.Length > 2000 ? buffer[^2000..] : buffer;
        _prompt = string.IsNullOrWhiteSpace(_prompt)
            ? $"Terminal output:\n```\n{tail}\n```"
            : $"{_prompt}\n\nTerminal output:\n```\n{tail}\n```";
        _activeTab = "chat";
    }

    // ------------------------------------------------------------------- git

    private async Task LoadChangesAsync()
    {
        if (_detail is null) return;
        var changes = await Bridge.GetGitChangesAsync(_detail.Id, _pageCts.Token);
        _gitIsRepo = changes.IsRepo;
        _gitChanges = changes.Files;
        _rejectConfirmPath = null;
        if (_diff is not null && _gitChanges.All(c => c.Path != _diff.Path))
        {
            _diff = null;
            _diffLines = [];
        }
    }

    private async Task LoadDiffAsync(string path)
    {
        if (_detail is null) return;
        _diff = await Bridge.GetGitDiffAsync(_detail.Id, path, _pageCts.Token);
        _diffLines = _diff?.DiffText.Split('\n') ?? [];
    }

    private async Task GitAcceptAsync(string path)
    {
        if (_detail is null) return;
        await Bridge.GitAcceptAsync(_detail.Id, path, _pageCts.Token);
        await LoadChangesAsync();
    }

    private async Task GitRejectAsync(string path)
    {
        if (_detail is null) return;
        await Bridge.GitRejectAsync(_detail.Id, path, _pageCts.Token);
        await LoadChangesAsync();
    }

    private static string DiffLineClass(string line) => line switch
    {
        _ when line.StartsWith("+++") || line.StartsWith("---") => "meta",
        _ when line.StartsWith("@@") => "hunk",
        _ when line.StartsWith('+') => "add",
        _ when line.StartsWith('-') => "del",
        _ when line.StartsWith("diff ") || line.StartsWith("index ") => "meta",
        _ => ""
    };

    private string ModelSuffix()
    {
        var m = _detail?.Model is { Length: > 0 } dm ? dm : _scopeModel;
        return m is not null ? $" · {m}" : "";
    }

    private string AttachTitle() =>
        _detail?.Caps is { Image: false, EmbeddedContext: false }
            ? "This agent advertises no attachment support — it may reject them"
            : "Attach images or text files (max 4 × 2 MB)";

    // ---------------------------------------------------- directory browser

    private async Task OpenBrowseAsync()
    {
        // Try native browser dir picker first (shows LOCAL filesystem).
        bool supportsLocal = false;
        try { supportsLocal = await JS.InvokeAsync<bool>("helmFs.supportsLocalPicker"); } catch { }

        if (supportsLocal)
        {
            string? localName = null;
            try { localName = await JS.InvokeAsync<string?>("helmFs.openLocalDirPicker"); } catch { }
            if (localName is not null)
            {
                // User picked a local dir — open server browser with a hint so they can find the mount point.
                _browseLocalName = localName;
                _browseOpen = true;
                await NavigateBrowseAsync(string.IsNullOrWhiteSpace(_newCwd) ? null : _newCwd.Trim());
                return;
            }
            // Cancelled — fall through to server browser.
        }

        _browseLocalName = null;
        _browseOpen = true;
        await NavigateBrowseAsync(string.IsNullOrWhiteSpace(_newCwd) ? null : _newCwd.Trim());
    }

    private async Task NavigateBrowseAsync(string? path)
    {
        _browseLoading = true;
        await InvokeAsync(StateHasChanged);
        var result = await Bridge.GetDirectoriesAsync(path, _pageCts.Token);
        _browsePath = result.Path;
        _browseParent = result.Parent;
        _browseDirs = _browseShowHidden
            ? result.Dirs
            : result.Dirs.Where(d => !d.StartsWith('.')).ToArray();
        _browseLoading = false;
        await InvokeAsync(StateHasChanged);
    }

    private Task BrowseIntoAsync(string dirName)
    {
        var full = string.IsNullOrEmpty(_browsePath)
            ? dirName
            : System.IO.Path.Combine(_browsePath, dirName);
        return NavigateBrowseAsync(full);
    }

    private async Task ToggleBrowseHiddenAsync()
    {
        _browseShowHidden = !_browseShowHidden;
        await NavigateBrowseAsync(_browsePath);
    }

    private void SelectBrowsedDir()
    {
        _newCwd = _browsePath;
        _browseOpen = false;
        _browseLocalName = null;
    }

    private void CloseBrowse()
    {
        _browseOpen = false;
        _browseLocalName = null;
    }

    // --------------------------------------------------- preconfiguration

    private async Task TogglePreconfigAsync()
    {
        _showPreconfig = !_showPreconfig;
        if (_showPreconfig && string.IsNullOrEmpty(_preconfigOtelUrl))
            _preconfigOtelUrl = await Bridge.GetScopeUrlAsync(_pageCts.Token);
    }

    // --------------------------------------------------------------- handoff

    private void StartEditTitle()
    {
        if (_detail is null) return;
        _titleDraft = _detail.Title;
        _editingTitle = true;
    }

    private async Task OnTitleKeyAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Escape") { _editingTitle = false; return; }
        if (e.Key != "Enter" || _detail is null) return;
        var title = _titleDraft.Trim();
        _editingTitle = false;
        if (title.Length is 0 or > 120 || title == _detail.Title) return;
        await Bridge.SetTitleAsync(_detail.Id, title, _pageCts.Token);
        _detail = _detail with { Title = title };   // event will confirm
        _sessions = await Bridge.GetSessionsAsync(_pageCts.Token);
    }

    private void ToggleHandoff()
    {
        _handoffOpen = !_handoffOpen;
        _handoffError = null;
        if (_handoffOpen && string.IsNullOrEmpty(_handoffAgentId))
            _handoffAgentId = _agents.FirstOrDefault(a => a.Id != _detail?.AgentId)?.Id
                              ?? _agents.FirstOrDefault()?.Id ?? "";
    }

    private async Task RunHandoffAsync()
    {
        if (_detail is null || string.IsNullOrEmpty(_handoffAgentId)) return;
        _handingOff = true;
        _handoffError = null;
        try
        {
            var (result, error) = await Bridge.HandoffAsync(_detail.Id, _handoffAgentId, null, _pageCts.Token);
            if (result is null)
            {
                _handoffError = error;
                return;
            }
            _handoffOpen = false;
            _sessions = await Bridge.GetSessionsAsync(_pageCts.Token);
            await SelectSessionAsync(result.Session.Id);
            _prompt = result.Context;   // prefilled — the user reviews and sends
        }
        finally
        {
            _handingOff = false;
        }
    }

    // ----------------------------------------------------------------- scope

    private async Task ToggleScopeAsync()
    {
        _scopeOpen = !_scopeOpen;
        if (_scopeOpen && _detail is not null)
        {
            _scope = null;
            _scope = await Bridge.GetScopeAsync(_detail.Id, _pageCts.Token);
            // Capture the model name from the best-matching Scope session (first = newest).
            _scopeModel = _scope?.Matches.FirstOrDefault(m => m.Model is not null)?.Model;
        }
    }

    private async Task OnComposerKeyAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && e.CtrlKey) await SendPromptAsync();
    }

    private async Task ResolvePermissionAsync(PermissionOptionDto option)
    {
        if (_detail is null || _pending is null) return;
        var allow = !option.Kind.Contains("reject", StringComparison.OrdinalIgnoreCase);
        await Bridge.ResolvePermissionAsync(_detail.Id, _pending.RequestKey, allow, option.OptionId, _pageCts.Token);
    }

    private async Task CancelTurnAsync()
    {
        if (_detail is not null) await Bridge.CancelAsync(_detail.Id, _pageCts.Token);
    }

    private async Task DeleteSessionAsync()
    {
        if (_detail is null) return;
        await Bridge.DeleteAsync(_detail.Id, _pageCts.Token);
        _detail = null;
        _selectedId = null;
        _transcript = [];
        _pending = null;
        _sessions = await Bridge.GetSessionsAsync(_pageCts.Token);
    }

    // ---------------------------------------------------------------- policy

    private async Task OnPolicyChangeAsync(ChangeEventArgs e)
    {
        var chosen = e.Value?.ToString();
        if (_detail is null || string.IsNullOrEmpty(chosen) || chosen == _detail.Policy) return;

        if (chosen == "yolo")
        {
            // Never silently: YOLO requires an explicit confirmation click.
            // Until confirmed, _detail.Policy is unchanged, so a re-render
            // snaps the select back.
            _yoloConfirmPending = true;
            return;
        }
        await ApplyPolicyAsync(chosen);
    }

    private async Task ApplyPolicyAsync(string policy)
    {
        if (_detail is null) return;
        await Bridge.SetPolicyAsync(_detail.Id, policy, _pageCts.Token);
        _detail = _detail with { Policy = policy };   // SSE will confirm + audit lands as entry
    }

    private async Task ConfirmYoloAsync()
    {
        _yoloConfirmPending = false;
        await ApplyPolicyAsync("yolo");
    }

    private void CancelYolo()
    {
        _yoloConfirmPending = false;
        _policySelectVersion++;
    }

    private static string PolicyLabel(string policy) => policy switch
    {
        "auto_read" => "auto-read",
        "yolo" => "YOLO",
        _ => "ask"
    };

    // --------------------------------------------------------------- history

    private async Task ToggleHistoryAsync()
    {
        _showHistory = !_showHistory;
        if (_showHistory)
        {
            _history = await Bridge.GetHistoryAsync(_pageCts.Token);
        }
        else
        {
            _archived = null;
            _resumeError = null;
        }
    }

    private void SelectArchived(ArchivedSessionDto archived)
    {
        _archived = archived;
        _resumeError = null;
        _streamCts?.Cancel();
        _selectedId = null;
        _detail = null;
    }

    private async Task ResumeArchivedAsync()
    {
        if (_archived is null || string.IsNullOrEmpty(_archived.NativeSessionId)) return;
        _resuming = true;
        _resumeError = null;
        try
        {
            var (session, error) = await Bridge.ResumeAsync(_archived.Id, null, _pageCts.Token);
            if (session is null)
            {
                _resumeError = error;
                return;
            }
            _sessions = await Bridge.GetSessionsAsync(_pageCts.Token);
            await SelectSessionAsync(session.Id);
        }
        finally
        {
            _resuming = false;
        }
    }

    private static string RoleClass(ChatEntryDto entry) => entry.Role switch
    {
        "user" => "user",
        "assistant" => "assistant",
        "tool" => "tool",
        _ => "system"
    };

    public void Dispose()
    {
        _termCts?.Cancel();
        _streamCts?.Cancel();
        _pageCts.Cancel();
        _pageCts.Dispose();
    }
}
