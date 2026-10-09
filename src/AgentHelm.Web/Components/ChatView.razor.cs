using AgentHelm.Web.Services;
using Microsoft.AspNetCore.Components;

namespace AgentHelm.Web.Components;

public partial class ChatView
{
    [Parameter] public IReadOnlyList<ChatEntryDto> Entries { get; set; } = [];
    [Parameter] public string Streaming { get; set; } = "";

    private static string RoleClass(ChatEntryDto entry) => entry.Role switch
    {
        "user" => "user",
        "assistant" => "assistant",
        "tool" => "tool",
        _ => "system"
    };
}
