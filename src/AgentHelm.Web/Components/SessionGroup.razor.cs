using Microsoft.AspNetCore.Components;

namespace AgentHelm.Web.Components;

public partial class SessionGroup
{
    [Parameter, EditorRequired] public string Name { get; set; } = "";
    [Parameter] public int Count { get; set; }
    [Parameter] public bool Expanded { get; set; } = true;
    [Parameter] public EventCallback OnToggle { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
