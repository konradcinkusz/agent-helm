using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AgentHelm.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AgentHelm.Web.Components;

public partial class ChatView
{
    [Parameter] public IReadOnlyList<ChatEntryDto> Entries { get; set; } = [];
    [Parameter] public string Streaming { get; set; } = "";
    [Inject] public IJSRuntime JS { get; set; } = default!;

    private ElementReference _scroll;

    protected override async Task OnAfterRenderAsync(bool firstRender) =>
        await JS.InvokeVoidAsync("helmChat.follow", _scroll);

    private static string RoleClass(ChatEntryDto entry) => entry.Role switch
    {
        "user" => "user",
        "assistant" => "assistant",
        "tool" => "tool",
        _ => "system"
    };

    private static IEnumerable<List<ChatEntryDto>> GroupRuns(IEnumerable<ChatEntryDto> entries)
    {
        List<ChatEntryDto>? run = null;
        foreach (var entry in entries)
        {
            var isTool = entry.Kind == "tool_call";
            if (isTool && run is not null)
            {
                run.Add(entry);
                continue;
            }
            if (run is not null)
            {
                yield return run;
                run = null;
            }
            if (isTool) run = new List<ChatEntryDto> { entry };
            else yield return new List<ChatEntryDto> { entry };
        }
        if (run is not null) yield return run;
    }
}

/// <summary>
/// Renders assistant text as HTML. Every character is HTML-encoded before any markup is
/// added back, so transcript text can never inject tags or script.
/// </summary>
internal static class ChatMarkdown
{
    private static readonly Regex CodeSpan = new("`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"\[([^\]]+)\]\((https?://[^\s)<>""]+)\)", RegexOptions.Compiled);
    private static readonly Regex Bold = new(@"\*\*(?=\S)(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex Italic = new(@"(?<![\w*])\*(?=\S)(.+?)\*(?![\w*])", RegexOptions.Compiled);
    private static readonly Regex Placeholder = new("\u0001(\\d+)\u0001", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^\s*[-*]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Ordered = new(@"^\s*\d+[.)]\s+(.*)$", RegexOptions.Compiled);

    public static string Render(string text)
    {
        var html = new StringBuilder();
        var paragraph = new List<string>();
        var list = (string?)null;
        var code = (StringBuilder?)null;

        void CloseParagraph()
        {
            if (paragraph.Count == 0) return;
            html.Append("<p>").Append(string.Join("<br>", paragraph.Select(Inline))).Append("</p>");
            paragraph.Clear();
        }

        void CloseList()
        {
            if (list is null) return;
            html.Append("</").Append(list).Append('>');
            list = null;
        }

        void OpenList(string tag)
        {
            if (list == tag) return;
            CloseList();
            html.Append('<').Append(tag).Append('>');
            list = tag;
        }

        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (code is not null)
            {
                if (trimmed.StartsWith("```"))
                {
                    html.Append("<pre><code>").Append(code).Append("</code></pre>");
                    code = null;
                }
                else
                {
                    code.Append(WebUtility.HtmlEncode(line)).Append('\n');
                }
                continue;
            }

            if (trimmed.StartsWith("```"))
            {
                CloseParagraph();
                CloseList();
                code = new StringBuilder();
                continue;
            }

            if (line.Trim().Length == 0)
            {
                CloseParagraph();
                CloseList();
                continue;
            }

            var heading = Heading.Match(line);
            if (heading.Success)
            {
                CloseParagraph();
                CloseList();
                html.Append("<h4>").Append(Inline(heading.Groups[2].Value)).Append("</h4>");
                continue;
            }

            var bullet = Bullet.Match(line);
            if (bullet.Success)
            {
                CloseParagraph();
                OpenList("ul");
                html.Append("<li>").Append(Inline(bullet.Groups[1].Value)).Append("</li>");
                continue;
            }

            var ordered = Ordered.Match(line);
            if (ordered.Success)
            {
                CloseParagraph();
                OpenList("ol");
                html.Append("<li>").Append(Inline(ordered.Groups[1].Value)).Append("</li>");
                continue;
            }

            CloseList();
            paragraph.Add(line.Trim());
        }

        if (code is not null) html.Append("<pre><code>").Append(code).Append("</code></pre>");
        CloseParagraph();
        CloseList();
        return html.ToString();
    }

    private static string Inline(string text)
    {
        var codeSpans = new List<string>();
        var html = WebUtility.HtmlEncode(text);
        html = CodeSpan.Replace(html, m =>
        {
            codeSpans.Add($"<code>{m.Groups[1].Value}</code>");
            return $"\u0001{codeSpans.Count - 1}\u0001";
        });
        html = Link.Replace(html, m =>
            $"<a href=\"{m.Groups[2].Value}\" target=\"_blank\" rel=\"noopener noreferrer\">{m.Groups[1].Value}</a>");
        html = Bold.Replace(html, "<strong>$1</strong>");
        html = Italic.Replace(html, "<em>$1</em>");
        return Placeholder.Replace(html, m => codeSpans[int.Parse(m.Groups[1].Value)]);
    }
}
