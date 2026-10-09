using AgentHelm.Web.Services;
using Microsoft.AspNetCore.Components;

namespace AgentHelm.Web.Components.Pages;

/// <summary>
/// Landing view: live and archived sessions bucketed by what needs the user.
/// Live sessions are polled like the rail; archived ones are refreshed less often
/// because the history endpoint reads every stored transcript.
/// </summary>
public partial class MyWork : IDisposable
{
    private enum WorkSection { All, Active, NeedsAttention, Done }

    private sealed record WorkItem(
        string Id, string Title, string AgentId, string Cwd, string Status, bool Pending,
        DateTimeOffset LastActivity, bool Archived)
    {
        public WorkSection Bucket => Archived ? WorkSection.Done
            : Pending || Status == "error" ? WorkSection.NeedsAttention
            : Status == "running" ? WorkSection.Active
            : WorkSection.Done;
    }

    private const int ArchivedRefreshEveryTicks = 20;

    private static readonly WorkSection[] Sections = Enum.GetValues<WorkSection>();

    [Inject] public NavigationManager Nav { get; set; } = default!;

    private readonly CancellationTokenSource _pageCts = new();
    private List<SessionSummaryDto> _live = [];
    private List<ArchivedSessionDto> _archived = [];
    private List<WorkItem> _items = [];
    private WorkSection _section = WorkSection.All;
    private bool _tableView;

    private List<WorkItem> Visible => _section == WorkSection.All
        ? _items
        : _items.Where(i => i.Bucket == _section).ToList();

    protected override async Task OnInitializedAsync()
    {
        _archived = await Bridge.GetHistoryAsync(_pageCts.Token);
        _live = await Bridge.GetSessionsAsync(_pageCts.Token);
        Rebuild();
        _ = PollAsync();
    }

    private async Task PollAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        var ticks = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(_pageCts.Token))
            {
                ticks++;
                var live = await Bridge.GetSessionsAsync(_pageCts.Token);
                var archived = ticks % ArchivedRefreshEveryTicks == 0
                    ? await Bridge.GetHistoryAsync(_pageCts.Token)
                    : null;
                await InvokeAsync(() =>
                {
                    _live = live;
                    if (archived is not null) _archived = archived;
                    Rebuild();
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException) { }
    }

    private void Rebuild()
    {
        _items = _live
            .Select(s => new WorkItem(s.Id, s.Title, s.AgentId, s.Cwd, s.Status, s.HasPendingPermission,
                s.LastActivity, Archived: false))
            .Concat(_archived.Select(a => new WorkItem(a.Id, a.Title, a.AgentId, a.Cwd, "archived", false,
                a.LastActivity, Archived: true)))
            .OrderByDescending(i => i.LastActivity)
            .ToList();
    }

    private int CountIn(WorkSection section) => section == WorkSection.All
        ? _items.Count
        : _items.Count(i => i.Bucket == section);

    private void Open(WorkItem item) =>
        Nav.NavigateTo(item.Archived ? $"/sessions/archived/{item.Id}" : $"/sessions/{item.Id}");

    private static string SectionLabel(WorkSection section) => section switch
    {
        WorkSection.All => "All",
        WorkSection.Active => "Active",
        WorkSection.NeedsAttention => "Needs attention",
        _ => "Done"
    };

    private static string PillClass(WorkItem item) => item.Bucket switch
    {
        WorkSection.NeedsAttention => "needs",
        WorkSection.Active => "active",
        _ => "done"
    };

    private static string StatusLabel(WorkItem item) =>
        item.Pending ? "⏳ permission" : item.Status;

    private static string RepoName(string cwd)
    {
        var trimmed = cwd.TrimEnd('/', '\\');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? cwd : name;
    }

    private static string Ago(DateTimeOffset when)
    {
        var span = DateTimeOffset.UtcNow - when;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} h ago";
        return when.LocalDateTime.ToString("MMM d, HH:mm");
    }

    public void Dispose()
    {
        _pageCts.Cancel();
        _pageCts.Dispose();
    }
}
