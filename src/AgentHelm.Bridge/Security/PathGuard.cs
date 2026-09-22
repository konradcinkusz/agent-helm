namespace AgentHelm.Bridge.Security;

/// <summary>
/// The working-directory rule behind the ACP file requests and the git
/// endpoints: a path may only name something inside the session's working
/// directory — both as written and as the operating system will resolve it.
/// The second half matters because a symbolic link inside the directory can
/// point anywhere; checking only the spelling of a path would let such a link
/// lead straight out.
/// </summary>
internal static class PathGuard
{
    /// <summary>
    /// Resolves <paramref name="path"/> — absolute, or relative to
    /// <paramref name="root"/> — and returns its full, normalised form, or
    /// null when it lies outside the root, lexically or once symbolic links
    /// are followed. <paramref name="allowRoot"/> decides whether the root
    /// itself counts as inside.
    /// </summary>
    public static string? Resolve(string root, string path, bool allowRoot)
    {
        var fullRoot = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(fullRoot, path));
        if (!IsWithin(fullRoot, full, allowRoot)) return null;
        try
        {
            return IsWithin(RealPath(fullRoot), RealPath(full), allowRoot) ? full : null;
        }
        // A link cycle, or a link we may not read: fail closed.
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static bool IsWithin(string root, string full, bool allowRoot)
    {
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.Ordinal)
            || (allowRoot && Path.TrimEndingDirectorySeparator(full) == Path.TrimEndingDirectorySeparator(root));
    }

    /// <summary>
    /// <paramref name="fullPath"/> with every symbolic link in it — file or
    /// directory, at any depth, chained, absolute or relative — replaced by
    /// what it points to, the way the operating system resolves the path when
    /// it is opened. Components that do not exist yet (a file about to be
    /// written) are kept as they are.
    /// </summary>
    internal static string RealPath(string fullPath)
    {
        const int maxLinks = 40;   // Linux's own limit; past it, assume a cycle
        var current = Path.GetPathRoot(fullPath)!;
        var remaining = new Stack<string>();
        PushComponents(remaining, fullPath[current.Length..]);
        var links = 0;

        while (remaining.Count > 0)
        {
            var part = remaining.Pop();
            if (part == ".") continue;
            if (part == "..") { current = Path.GetDirectoryName(current) ?? current; continue; }

            var next = Path.Combine(current, part);
            FileSystemInfo entry = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            if (entry.LinkTarget is not { } target) { current = next; continue; }

            if (++links > maxLinks)
                throw new IOException($"Too many levels of symbolic links: {fullPath}");
            // Walk the target in place of the link. A relative target starts
            // from the link's own directory, which is `current`.
            if (Path.IsPathRooted(target))
            {
                current = Path.GetPathRoot(target)!;
                target = target[current.Length..];
            }
            PushComponents(remaining, target);
        }
        return current;
    }

    /// <summary>Pushes the components of a path so that the first one is on top.</summary>
    private static void PushComponents(Stack<string> stack, string path)
    {
        var parts = path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        for (var i = parts.Length - 1; i >= 0; i--) stack.Push(parts[i]);
    }
}
