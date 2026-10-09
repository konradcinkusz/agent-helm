using Microsoft.AspNetCore.Components;

namespace AgentHelm.Web.Components;

public partial class AppShell
{
    [Parameter] public RenderFragment? Sidebar { get; set; }
    [Parameter] public RenderFragment? Main { get; set; }
    [Parameter] public RenderFragment? Dock { get; set; }
}
