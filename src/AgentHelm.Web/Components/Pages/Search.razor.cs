using AgentHelm.Web.Services;
using Microsoft.AspNetCore.Components;
using System.Net.Http;
using System.Text.Json;

namespace AgentHelm.Web.Components.Pages;

/// <summary>
/// Filters live and archived sessions in memory. Transcripts are loaded once
/// when the page opens; typing only re-filters what is already on the page.
/// </summary>
public partial class Search : IDisposable
{
    private const int ExcerptRadius = 60;

    private readonly CancellationTokenSource _cts = new();
    private List<SearchItem> _live = [];
    private List<SearchItem> _archived = [];
    private bool _loading = true;
    private string _query = "";

    private sealed record SearchItem(
        string Id, bool IsArchived, string AgentId, string Title, string Meta, List<ChatEntryDto> Transcript);

    private sealed record Excerpt(string Before, string Hit, string After);

    private sealed record SearchHit(SearchItem Item, Excerpt? Excerpt);

    private List<SearchHit> LiveHits => Filter(_live);

    private List<SearchHit> ArchivedHits => Filter(_archived);

    protected override async Task OnInitializedAsync()
    {
        var sessions = await Bridge.GetSessionsAsync(_cts.Token);
        var details = await Task.WhenAll(sessions.Select(s => LoadDetailAsync(s.Id)));
        _live = sessions
            .Select((s, i) => new SearchItem(s.Id, false, s.AgentId, s.Title, s.Status, details[i]?.Transcript ?? []))
            .ToList();

        var history = await Bridge.GetHistoryAsync(_cts.Token);
        _archived = history
            .Select(a => new SearchItem(a.Id, true, a.AgentId, a.Title,
                a.LastActivity.LocalDateTime.ToString("MMM d, HH:mm"), a.Transcript))
            .ToList();

        _loading = false;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private async Task<SessionDetailDto?> LoadDetailAsync(string id)
    {
        try
        {
            return await Bridge.GetSessionAsync(id, _cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return null;
        }
    }

    private List<SearchHit> Filter(IEnumerable<SearchItem> items)
    {
        var query = _query.Trim();
        return items.Select(item => Match(item, query)).OfType<SearchHit>().ToList();
    }

    private static SearchHit? Match(SearchItem item, string query)
    {
        if (query.Length == 0) return new(item, null);
        if (Contains(item.Title, query) || Contains(item.AgentId, query)) return new(item, null);

        foreach (var entry in item.Transcript)
        {
            if (MakeExcerpt(entry.Text, query) is { } excerpt) return new(item, excerpt);
        }
        return null;
    }

    private static bool Contains(string text, string query) =>
        text.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static Excerpt? MakeExcerpt(string text, string query)
    {
        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;

        var start = Math.Max(0, index - ExcerptRadius);
        var end = Math.Min(text.Length, index + query.Length + ExcerptRadius);
        return new Excerpt(
            (start > 0 ? "…" : "") + Flatten(text[start..index]),
            text.Substring(index, query.Length),
            Flatten(text[(index + query.Length)..end]) + (end < text.Length ? "…" : ""));
    }

    private static string Flatten(string text) => text.ReplaceLineEndings(" ");

    private static string HrefOf(SearchItem item) =>
        item.IsArchived
            ? $"/?archived={Uri.EscapeDataString(item.Id)}"
            : $"/?session={Uri.EscapeDataString(item.Id)}";
}
