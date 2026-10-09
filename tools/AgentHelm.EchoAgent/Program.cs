// Standalone entry point (`dotnet run --project tools/AgentHelm.EchoAgent`).
// The agent itself lives in EchoAgentHost.cs.
await AgentHelm.EchoAgent.EchoAgentHost.RunAsync();
