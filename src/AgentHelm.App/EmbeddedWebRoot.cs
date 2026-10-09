using System.Reflection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace AgentHelm.App;

/// <summary>
/// Serves the UI's static files (app.css, chat.js, the scoped CSS bundle, ...) from the assembly's
/// embedded resources named "wwwroot/&lt;path&gt;". Read straight from the assembly, so they work
/// inside a single-file executable without extracting anything. The resources are listed in
/// AgentHelm.App.csproj.
/// </summary>
internal sealed class EmbeddedWebRoot : IFileProvider
{
    private const string Prefix = "wwwroot/";
    private static readonly DateTimeOffset Started = DateTimeOffset.UtcNow;

    private readonly Assembly _assembly;
    private readonly Dictionary<string, string> _resources;

    public EmbeddedWebRoot(Assembly assembly)
    {
        _assembly = assembly;
        _resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
            .ToDictionary(name => name[Prefix.Length..], name => name, StringComparer.Ordinal);
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        var path = subpath.TrimStart('/');
        if (_resources.TryGetValue(path, out var resource))
            return new EmbeddedFile(_assembly, resource, path);
        return new NotFoundFileInfo(subpath);
    }

    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

    private sealed class EmbeddedFile : IFileInfo
    {
        private readonly Assembly _assembly;
        private readonly string _resource;

        public EmbeddedFile(Assembly assembly, string resource, string name)
        {
            _assembly = assembly;
            _resource = resource;
            Name = name;
        }

        public bool Exists => true;
        public bool IsDirectory => false;
        public DateTimeOffset LastModified => Started;
        public string Name { get; }
        public string? PhysicalPath => null;

        public long Length
        {
            get
            {
                using var stream = CreateReadStream();
                return stream.Length;
            }
        }

        public Stream CreateReadStream() =>
            _assembly.GetManifestResourceStream(_resource)
            ?? throw new FileNotFoundException($"Embedded UI file '{_resource}' is missing.");
    }
}
