using AgentHelm.Web.Services;
using Microsoft.AspNetCore.Components;

namespace AgentHelm.Web.Components;

public partial class SessionSidebar
{
    [Parameter] public IReadOnlyList<SessionSummaryDto> Sessions { get; set; } = [];
    [Parameter] public IReadOnlyList<ArchivedSessionDto> History { get; set; } = [];
    [Parameter] public bool ShowHistory { get; set; }
    [Parameter] public string? SelectedId { get; set; }
    [Parameter] public string? ArchivedId { get; set; }
    [Parameter] public EventCallback OnToggleHistory { get; set; }
    [Parameter] public EventCallback OnToggleNewSession { get; set; }
    [Parameter] public EventCallback<string> OnSelectSession { get; set; }
    [Parameter] public EventCallback<ArchivedSessionDto> OnSelectArchived { get; set; }

    private bool _collapsed;

    private void ToggleCollapsed() => _collapsed = !_collapsed;

    private static string PolicyLabel(string policy) => policy switch
    {
        "auto_read" => "auto-read",
        "yolo" => "YOLO",
        _ => "ask"
    };
}
