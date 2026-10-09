using AgentHelm.Web.Services;
using Microsoft.AspNetCore.Components;

namespace AgentHelm.Web.Components;

public partial class ScopePanel
{
    [Parameter] public ScopeResultDto? Scope { get; set; }
}
