using AgentHelm.Web.Services;
using Microsoft.AspNetCore.Components;

namespace AgentHelm.Web.Components;

public partial class SessionSidebar
{
    [Parameter] public IReadOnlyList<AgentDto> Agents { get; set; } = [];
    [Parameter] public IReadOnlyList<SessionSummaryDto> Sessions { get; set; } = [];
    [Parameter] public IReadOnlyList<ArchivedSessionDto> History { get; set; } = [];
    [Parameter] public IReadOnlyList<ProviderModelDto> NewAgentModels { get; set; } = [];
    [Parameter] public bool ShowHistory { get; set; }
    [Parameter] public bool ShowNewSession { get; set; }
    [Parameter] public string? SelectedId { get; set; }
    [Parameter] public string? ArchivedId { get; set; }
    [Parameter] public string NewAgentId { get; set; } = "";
    [Parameter] public EventCallback<ChangeEventArgs> OnNewAgentChanged { get; set; }
    [Parameter] public string NewCwd { get; set; } = "";
    [Parameter] public EventCallback<string> NewCwdChanged { get; set; }
    [Parameter] public string? NewTitle { get; set; }
    [Parameter] public EventCallback<string?> NewTitleChanged { get; set; }
    [Parameter] public string? NewModel { get; set; }
    [Parameter] public EventCallback<string?> NewModelChanged { get; set; }
    [Parameter] public bool Creating { get; set; }
    [Parameter] public string? CreateError { get; set; }
    [Parameter] public bool ShowPreconfig { get; set; }
    [Parameter] public string PreconfigOtelUrl { get; set; } = "";
    [Parameter] public EventCallback<string> PreconfigOtelUrlChanged { get; set; }
    [Parameter] public EventCallback OnToggleHistory { get; set; }
    [Parameter] public EventCallback OnToggleNewSession { get; set; }
    [Parameter] public EventCallback OnBrowse { get; set; }
    [Parameter] public EventCallback OnCreate { get; set; }
    [Parameter] public EventCallback OnTogglePreconfig { get; set; }
    [Parameter] public EventCallback<string> OnSelectSession { get; set; }
    [Parameter] public EventCallback<ArchivedSessionDto> OnSelectArchived { get; set; }

    private Task NewAgentChangedAsync(ChangeEventArgs e) => OnNewAgentChanged.InvokeAsync(e);

    private Task CwdChangedAsync(ChangeEventArgs e) => NewCwdChanged.InvokeAsync(e.Value?.ToString() ?? "");

    private Task TitleChangedAsync(ChangeEventArgs e) => NewTitleChanged.InvokeAsync(e.Value?.ToString());

    private Task ModelChangedAsync(ChangeEventArgs e) => NewModelChanged.InvokeAsync(e.Value?.ToString());

    private Task OtelUrlChangedAsync(ChangeEventArgs e) => PreconfigOtelUrlChanged.InvokeAsync(e.Value?.ToString() ?? "");

    private static string PolicyLabel(string policy) => policy switch
    {
        "auto_read" => "auto-read",
        "yolo" => "YOLO",
        _ => "ask"
    };
}
