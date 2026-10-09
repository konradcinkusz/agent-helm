using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace AgentHelm.Web.Components;

public partial class TerminalPanel
{
    [Parameter] public string TermInput { get; set; } = "";
    [Parameter] public EventCallback<string> TermInputChanged { get; set; }
    [Parameter] public bool IsPty { get; set; }
    [Parameter] public EventCallback<KeyboardEventArgs> OnKeyDown { get; set; }
    [Parameter] public EventCallback OnInsertOutput { get; set; }

    private Task TermInputChangedAsync(ChangeEventArgs e) => TermInputChanged.InvokeAsync(e.Value?.ToString() ?? "");
}
