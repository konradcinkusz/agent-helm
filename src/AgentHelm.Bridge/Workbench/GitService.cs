using System.Diagnostics;
using System.Text;
using AgentHelm.Bridge.Security;

namespace AgentHelm.Bridge.Workbench;

// Git diff viewer backend. Design decisions:
// - Plain `git` subprocess, no LibGit2Sharp: zero NuGet (sandbox-verifiable),
//   and the CLI is the one tool guaranteed present wherever agents run.
// - Accept = `git add` (stage it — the user keeps the change and marks it
//   reviewed). Reject = `git checkout HEAD -- <path>` for tracked files,
//   File.Delete for untracked ones. Reject re-checks the file's status itself
//   rather than trusting the caller: the difference between "revert" and
//   "delete" is not something to take from a browser request.
// - Every path that can reach File.Delete goes through the same guard the
//   ACP client uses: nothing outside the session working directory.

public sealed record GitResult(int ExitCode, string StdOut, string StdErr);

public interface IGitRunner
{
    Task<GitResult> RunAsync(string cwd, string[] args, CancellationToken ct);
}

public sealed class ProcessGitRunner : IGitRunner
{
    public async Task<GitResult> RunAsync(string cwd, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return new GitResult(process.ExitCode, await stdout, await stderr);
    }
}

public sealed record GitFileChange(string Path, string Status, bool Untracked);

public sealed record GitFileDiff(string Path, string Status, string DiffText, int Additions, int Deletions);

public sealed class GitService(IGitRunner runner)
{
    public async Task<bool> IsRepoAsync(string cwd, CancellationToken ct)
    {
        try
        {
            var result = await runner.RunAsync(cwd, ["rev-parse", "--is-inside-work-tree"], ct);
            return result.ExitCode == 0 && result.StdOut.Trim() == "true";
        }
        catch { return false; }
    }

    /// <summary>Working-tree changes vs HEAD, parsed from `git status --porcelain -z`.</summary>
    public async Task<List<GitFileChange>> ChangesAsync(string cwd, CancellationToken ct)
    {
        var result = await runner.RunAsync(cwd, ["status", "--porcelain", "-z"], ct);
        if (result.ExitCode != 0) throw new InvalidOperationException($"git status failed: {result.StdErr}");
        return ParsePorcelain(result.StdOut);
    }

    /// <summary>
    /// NUL-separated porcelain v1. Each entry is "XY PATH"; rename entries
    /// ("R" in X) are followed by the ORIGINAL path as a separate NUL token,
    /// which must be consumed or every following entry shifts by one.
    /// </summary>
    internal static List<GitFileChange> ParsePorcelain(string raw)
    {
        var changes = new List<GitFileChange>();
        var tokens = raw.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Length < 4) continue;               // "XY p" minimum
            var x = token[0];
            var y = token[1];
            var path = token[3..];
            var untracked = x == '?' && y == '?';
            var status = untracked ? "untracked" : StatusWord(x, y);
            changes.Add(new GitFileChange(path, status, untracked));
            if (x == 'R' || x == 'C') i++;                // consume original path token
        }
        return changes;
    }

    private static string StatusWord(char x, char y)
    {
        var significant = y != ' ' ? y : x;               // worktree change wins over staged
        return significant switch
        {
            'M' => "modified",
            'A' => "added",
            'D' => "deleted",
            'R' => "renamed",
            'C' => "copied",
            'T' => "typechange",
            _ => "changed"
        };
    }

    public async Task<GitFileDiff> DiffAsync(string cwd, string path, CancellationToken ct)
    {
        GuardPath(cwd, path);
        if (await IsUntrackedAsync(cwd, path, ct))
        {
            // Untracked files have no diff vs HEAD — synthesize an all-added view.
            var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(cwd, path));
            var lines = File.Exists(full) ? await File.ReadAllLinesAsync(full, ct) : [];
            var text = $"+++ b/{path}\n" + string.Join('\n', lines.Select(l => "+" + l));
            return new GitFileDiff(path, "untracked", text, lines.Length, 0);
        }

        var result = await runner.RunAsync(cwd, ["diff", "HEAD", "--no-color", "--", path], ct);
        if (result.ExitCode != 0) throw new InvalidOperationException($"git diff failed: {result.StdErr}");
        var (add, del) = CountChanges(result.StdOut);
        return new GitFileDiff(path, "modified", result.StdOut, add, del);
    }

    internal static (int Additions, int Deletions) CountChanges(string diffText)
    {
        int add = 0, del = 0;
        foreach (var line in diffText.Split('\n'))
        {
            if (line.StartsWith('+') && !line.StartsWith("+++")) add++;
            else if (line.StartsWith('-') && !line.StartsWith("---")) del++;
        }
        return (add, del);
    }

    /// <summary>Accept = stage. The change stays; it is marked as reviewed.</summary>
    public async Task AcceptAsync(string cwd, string path, CancellationToken ct)
    {
        GuardPath(cwd, path);
        var result = await runner.RunAsync(cwd, ["add", "--", path], ct);
        if (result.ExitCode != 0) throw new InvalidOperationException($"git add failed: {result.StdErr}");
    }

    /// <summary>
    /// Reject = make it not have happened: revert tracked files to HEAD,
    /// delete untracked ones. The untracked/tracked decision is re-derived
    /// here — never trusted from the request.
    /// </summary>
    public async Task RejectAsync(string cwd, string path, CancellationToken ct)
    {
        GuardPath(cwd, path);
        if (await IsUntrackedAsync(cwd, path, ct))
        {
            var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(cwd, path));
            if (File.Exists(full)) File.Delete(full);
            return;
        }
        var result = await runner.RunAsync(cwd, ["checkout", "HEAD", "--", path], ct);
        if (result.ExitCode != 0) throw new InvalidOperationException($"git checkout failed: {result.StdErr}");
    }

    private async Task<bool> IsUntrackedAsync(string cwd, string path, CancellationToken ct)
    {
        var result = await runner.RunAsync(cwd, ["status", "--porcelain", "-z", "--", path], ct);
        return result.ExitCode == 0 && result.StdOut.StartsWith("??", StringComparison.Ordinal);
    }

    /// <summary>Same invariant as the ACP fs guard: nothing outside the session cwd, links included.</summary>
    internal static void GuardPath(string cwd, string relative)
    {
        if (PathGuard.Resolve(cwd, relative, allowRoot: false) is null)
            throw new ArgumentException($"Path escapes the session working directory: {relative}");
    }
}
