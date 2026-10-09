using AgentHelm.Bridge;

var builder = WebApplication.CreateBuilder(args);
// SECURITY: loopback-only by default. Exposing a tool that executes agent
// actions to the network is an explicit, documented decision — not a default.
builder.WebHost.UseUrls(builder.Configuration["AgentHelm:Urls"] ?? "http://127.0.0.1:5199");

builder.AddAgentHelmBridge();

var app = builder.Build();
app.MapAgentHelmBridge();
app.Run();

record CreateSessionRequest(string AgentId, string? Cwd, string? Title, string? Policy, string? Model, bool IsChat = false);
record PolicyRequest(string Policy);
record TitleRequest(string Title);
record ResumeRequest(string? Title);
record PromptRequest(string Text, List<AgentHelm.Bridge.Agents.Acp.PromptAttachment>? Attachments);
record GitPathRequest(string Path);
record HandoffRequest(string AgentId, string? Title);
record TerminalInputRequest(string Text);
record PermissionDecision(string RequestKey, bool Allow, string? OptionId);
record ScopeUrlRequest(string Url);
