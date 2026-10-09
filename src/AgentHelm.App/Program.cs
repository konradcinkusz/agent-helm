using AgentHelm.App;
using AgentHelm.Bridge;
using AgentHelm.Web;
using Microsoft.AspNetCore.StaticFiles;

// The demo agent is this same executable run with a sub-command; the catalog's "echo" entry
// starts it that way (see SingleFileHost).
if (args.Length > 0 && args[0] == "echo-agent")
{
    await AgentHelm.EchoAgent.EchoAgentHost.RunAsync();
    return 0;
}

var noBrowser = args.Contains("--no-browser");
var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--no-browser").ToArray());
var address = SingleFileHost.Configure(builder);
builder.AddAgentHelmBridge();
builder.AddAgentHelmWeb();

var app = builder.Build();
// The UI's static files come from the executable itself (see EmbeddedWebRoot).
app.UseStaticFiles(new StaticFileOptions { FileProvider = new EmbeddedWebRoot(typeof(SingleFileHost).Assembly) });
app.MapAgentHelmBridge();
app.MapAgentHelmWeb();

app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine($"AgentHelm is running at {address}/ (Ctrl+C to stop)");
    if (!noBrowser) SingleFileHost.OpenBrowser($"{address}/");
});

app.Run();
return 0;
