using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration.Memory;

namespace AgentHelm.App;

/// <summary>
/// Puts the Bridge API and the UI into one process on one loopback URL, and gives the echo
/// agent a command that runs this executable. Nothing here depends on the single-file build:
/// `dotnet run` behaves the same way.
/// </summary>
internal static class SingleFileHost
{
    internal const string DefaultUrl = "http://127.0.0.1:5199";

    // The Bridge's own appsettings.json, embedded by AgentHelm.App.csproj.
    private const string BridgeDefaultsResource = "bridge-appsettings.json";

    /// <summary>
    /// Applies the defaults this host needs, sets the listening URLs and returns the address the
    /// host answers on. The UI's BridgeClient uses that address as its Bridge:BaseUrl.
    /// </summary>
    public static string Configure(WebApplicationBuilder builder)
    {
        var config = builder.Configuration;

        // Inserted at the front, so these are the lowest-precedence sources: environment
        // variables, the command line and an appsettings.json next to the file still override them.
        var defaults = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        using (var stream = typeof(SingleFileHost).Assembly.GetManifestResourceStream(BridgeDefaultsResource)
            ?? throw new InvalidOperationException($"Embedded resource '{BridgeDefaultsResource}' is missing."))
        using (var reader = new StreamReader(stream))
        {
            var root = JsonNode.Parse(reader.ReadToEnd())
                ?? throw new InvalidOperationException("The embedded Bridge defaults are empty.");
            PointEchoAgentAtThisExecutable(root);
            Flatten(root, string.Empty, defaults);
        }
        config.Sources.Insert(0, new MemoryConfigurationSource { InitialData = defaults });

        // --urls wins, then AgentHelm:Urls (env, appsettings), then the default.
        var urls = config["urls"] ?? config["AgentHelm:Urls"] ?? DefaultUrl;
        builder.WebHost.UseUrls(urls);
        var address = OwnAddress(urls);

        // The UI talks to this same host, so its BridgeClient defaults to our own address and to
        // the API token the Bridge checks. Both are defaults; an explicit Bridge:BaseUrl wins.
        config.Sources.Insert(0, new MemoryConfigurationSource
        {
            InitialData = new Dictionary<string, string?>
            {
                ["Bridge:BaseUrl"] = address,
                ["Bridge:ApiToken"] = config["AgentHelm:ApiToken"] ?? "",
            }
        });
        return address;
    }

    /// <summary>The address to reach this host on: its first URL, with wildcard hosts replaced by loopback.</summary>
    internal static string OwnAddress(string urls)
    {
        var first = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? DefaultUrl;
        try
        {
            var uri = new Uri(first);
            var wildcard = uri.Host is "0.0.0.0" or "+" or "*" or "[::]" or "::";
            var host = wildcard ? "127.0.0.1" : uri.Host;
            return $"{uri.Scheme}://{host}:{uri.Port}";
        }
        catch (UriFormatException)
        {
            return DefaultUrl;
        }
    }

    /// <summary>Opens the browser on the UI. Best effort: a missing opener or a headless machine is not an error.</summary>
    public static void OpenBrowser(string url)
    {
        try
        {
            var start = OperatingSystem.IsWindows()
                ? new ProcessStartInfo(url) { UseShellExecute = true }
                : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", url) { UseShellExecute = false };
            using var process = Process.Start(start);
        }
        catch (Exception)
        {
            // The address is printed as well, so the user can open it by hand.
        }
    }

    private static void PointEchoAgentAtThisExecutable(JsonNode root)
    {
        var agents = root["AgentHelm"]?["Agents"]?.AsArray();
        if (agents is null) return;
        var (command, args) = SelfInvocation();
        foreach (var agent in agents)
        {
            if (agent is not JsonObject spec || spec["Id"]?.GetValue<string>() != "echo") continue;
            spec["Command"] = JsonValue.Create(command);
            spec["Args"] = new JsonArray(args.Select(arg => (JsonNode?)JsonValue.Create(arg)).ToArray());
        }
    }

    /// <summary>
    /// The command that starts this program as the echo agent. Under `dotnet run` (or
    /// `dotnet AgentHelm.App.dll`) the host is dotnet and the program is its first argument.
    /// </summary>
    private static (string Command, string[] Args) SelfInvocation()
    {
        var path = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot find the path of this executable.");
        if (Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return (path, new[] { Environment.GetCommandLineArgs()[0], "echo-agent" });
        return (path, new[] { "echo-agent" });
    }

    // Flattens JSON to configuration keys the way the JSON provider does ("AgentHelm:Agents:0:Id").
    private static void Flatten(JsonNode? node, string prefix, IDictionary<string, string?> into)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj)
                    Flatten(child, Join(prefix, key), into);
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                    Flatten(array[i], Join(prefix, i.ToString()), into);
                break;
            case JsonValue value:
                into[prefix] = value.TryGetValue<string>(out var text) ? text : value.ToJsonString();
                break;
        }
    }

    private static string Join(string prefix, string key) => prefix.Length == 0 ? key : prefix + ":" + key;
}
