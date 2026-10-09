using AgentHelm.Web.Components;
using AgentHelm.Web.Services;

namespace AgentHelm.Web;

/// <summary>
/// The Blazor UI composed into a host. Program.cs runs it on its own; AgentHelm.App
/// runs it in the same process as the Bridge API.
/// </summary>
public static class AgentHelmWebExtensions
{
    public static WebApplicationBuilder AddAgentHelmWeb(this WebApplicationBuilder builder)
    {
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddSingleton<BridgeClient>();
        return builder;
    }

    public static WebApplication MapAgentHelmWeb(this WebApplication app)
    {
        app.UseStaticFiles();
        app.UseAntiforgery();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        return app;
    }
}
