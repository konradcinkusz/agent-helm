using AgentHelm.App;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace AgentHelm.Tests;

public sealed class SingleFileHostTests
{
    [Fact]
    public void Own_address_replaces_wildcard_hosts_and_takes_the_first_url()
    {
        Assert.Equal("http://127.0.0.1:5300", SingleFileHost.OwnAddress("http://0.0.0.0:5300;http://[::1]:5300"));
        Assert.Equal("http://127.0.0.1:5199", SingleFileHost.OwnAddress("http://*:5199"));
        Assert.Equal("http://localhost:5199", SingleFileHost.OwnAddress("http://localhost:5199"));
    }

    [Fact]
    public void The_echo_agent_runs_this_executable_and_the_bridge_defaults_to_its_own_address()
    {
        var builder = WebApplication.CreateBuilder();
        var address = SingleFileHost.Configure(builder);

        Assert.Equal(SingleFileHost.DefaultUrl, address);
        Assert.Equal(address, builder.Configuration["Bridge:BaseUrl"]);

        var echo = builder.Configuration.GetSection("AgentHelm:Agents").GetChildren()
            .Single(agent => agent["Id"] == "echo");
        var args = echo.GetSection("Args").GetChildren().Select(arg => arg.Value).ToList();
        Assert.Equal(Environment.ProcessPath, echo["Command"]);
        Assert.Contains("echo-agent", args);
        Assert.DoesNotContain(args, arg => arg is not null && arg.Contains("${AGENTHELM_DIR}"));
    }
}
