using System.Text.RegularExpressions;
using AgentHelm.Web.Services;
using Microsoft.AspNetCore.Components;

namespace AgentHelm.Web.Components;

public partial class ChangesPanel
{
    private static readonly Regex HunkHeader = new(@"^@@ -(\d+)(?:,\d+)? \+(\d+)", RegexOptions.Compiled);

    [Parameter] public bool IsRepo { get; set; } = true;
    [Parameter] public IReadOnlyList<GitFileChangeDto> Changes { get; set; } = [];
    [Parameter] public GitFileDiffDto? Diff { get; set; }
    [Parameter] public string[] DiffLines { get; set; } = [];
    [Parameter] public string? RejectConfirmPath { get; set; }
    [Parameter] public EventCallback OnRefresh { get; set; }
    [Parameter] public EventCallback<string> OnView { get; set; }
    [Parameter] public EventCallback<string> OnAccept { get; set; }
    [Parameter] public EventCallback<string> OnAskAgent { get; set; }
    [Parameter] public EventCallback<string> OnRejectRequest { get; set; }
    [Parameter] public EventCallback OnRejectCancel { get; set; }
    [Parameter] public EventCallback<string> OnRejectConfirm { get; set; }

    private readonly Dictionary<string, (int Additions, int Deletions)> _stats = new();
    private List<DiffRow> _diffRows = [];
    private string? _expandedPath;

    private sealed record DiffRow(string Kind, int? OldLine, int? NewLine, string Text);

    protected override void OnParametersSet()
    {
        if (Diff is not null)
        {
            _stats[Diff.Path] = (Diff.Additions, Diff.Deletions);
        }
        _diffRows = ParseDiff(DiffLines);
    }

    private async Task ToggleAsync(string path)
    {
        if (_expandedPath == path)
        {
            _expandedPath = null;
            return;
        }
        _expandedPath = path;
        await OnView.InvokeAsync(path);
    }

    private static List<DiffRow> ParseDiff(string[] lines)
    {
        var rows = new List<DiffRow>(lines.Length);
        var oldNo = 0;
        var newNo = 0;
        var inHunk = false;
        foreach (var line in lines)
        {
            if (line.Length == 0) continue;
            if (line.StartsWith("@@"))
            {
                var match = HunkHeader.Match(line);
                if (match.Success)
                {
                    oldNo = int.Parse(match.Groups[1].Value);
                    newNo = int.Parse(match.Groups[2].Value);
                }
                inHunk = true;
                rows.Add(new DiffRow("hunk", null, null, line));
            }
            else if (!inHunk || line[0] == '\\')
            {
                rows.Add(new DiffRow("meta", null, null, line));
            }
            else if (line[0] == '+')
            {
                rows.Add(new DiffRow("add", null, newNo++, line));
            }
            else if (line[0] == '-')
            {
                rows.Add(new DiffRow("del", oldNo++, null, line));
            }
            else
            {
                rows.Add(new DiffRow("ctx", oldNo++, newNo++, line));
            }
        }
        return rows;
    }
}
