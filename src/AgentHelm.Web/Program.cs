using AgentHelm.Web;

var builder = WebApplication.CreateBuilder(args);
builder.AddAgentHelmWeb();

var app = builder.Build();
app.MapAgentHelmWeb();
app.Run();
