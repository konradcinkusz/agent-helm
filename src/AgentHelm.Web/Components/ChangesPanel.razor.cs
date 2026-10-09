using AgentHelm.Web.Services;
using Microsoft.AspNetCore.Components;

namespace AgentHelm.Web.Components;

public partial class ChangesPanel
{
    [Parameter] public bool IsRepo { get; set; } = true;
    [Parameter] public IReadOnlyList<GitFileChangeDto> Changes { get; set; } = [];
    [Parameter] public GitFileDiffDto? Diff { get; set; }
    [Parameter] public string[] DiffLines { get; set; } = [];
    [Parameter] public string? RejectConfirmPath { get; set; }
    [Parameter] public EventCallback OnRefresh { get; set; }
    [Parameter] public EventCallback<string> OnView { get; set; }
    [Parameter] public EventCallback<string> OnAccept { get; set; }
    [Parameter] public EventCallback<string> OnRejectRequest { get; set; }
    [Parameter] public EventCallback OnRejectCancel { get; set; }
    [Parameter] public EventCallback<string> OnRejectConfirm { get; set; }

    private static string DiffLineClass(string line) => line switch
    {
        _ when line.StartsWith("+++") || line.StartsWith("---") => "meta",
        _ when line.StartsWith("@@") => "hunk",
        _ when line.StartsWith('+') => "add",
        _ when line.StartsWith('-') => "del",
        _ when line.StartsWith("diff ") || line.StartsWith("index ") => "meta",
        _ => ""
    };
}
