using AgentHelm.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace AgentHelm.Web.Components;

public partial class SessionHeader
{
    [Parameter] public SessionDetailDto Session { get; set; } = default!;
    [Parameter] public string? ScopeModel { get; set; }
    [Parameter] public bool EditingTitle { get; set; }
    [Parameter] public string TitleDraft { get; set; } = "";
    [Parameter] public EventCallback<string> TitleDraftChanged { get; set; }
    [Parameter] public string ActiveTab { get; set; } = "chat";
    [Parameter] public EventCallback<string> OnSwitchTab { get; set; }
    [Parameter] public EventCallback OnStartEditTitle { get; set; }
    [Parameter] public EventCallback<KeyboardEventArgs> OnTitleKey { get; set; }
    [Parameter] public EventCallback OnToggleScope { get; set; }
    [Parameter] public EventCallback OnToggleHandoff { get; set; }
    [Parameter] public EventCallback OnStop { get; set; }
    [Parameter] public EventCallback OnDelete { get; set; }

    private bool _menuOpen;

    private Task TitleInputAsync(ChangeEventArgs e) => TitleDraftChanged.InvokeAsync(e.Value?.ToString() ?? "");

    private async Task RunAsync(EventCallback callback)
    {
        _menuOpen = false;
        await callback.InvokeAsync();
    }

    private string RepoName() =>
        Session.Cwd.TrimEnd('/', '\\').Split('/', '\\').LastOrDefault(s => s.Length > 0) ?? Session.Cwd;

    private string ModeLabel() => Session.Policy switch
    {
        "yolo" => "Autopilot",
        "auto_read" => "Plan",
        _ => "Interactive",
    };

    private string ModelSuffix()
    {
        var m = Session.Model is { Length: > 0 } dm ? dm : ScopeModel;
        return m is not null ? $" · {m}" : "";
    }
}
