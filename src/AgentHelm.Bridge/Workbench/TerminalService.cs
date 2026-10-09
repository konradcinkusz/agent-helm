using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using AgentHelm.Bridge.Sessions;

namespace AgentHelm.Bridge.Workbench;

// Integrated terminal. Honest design note: M2 shipped a SHELL PIPE, not a
// PTY — a real cross-platform PTY in .NET means ConPTY/forkpty interop or a
// native package, deliberately out of scope for zero dependencies. Through a
// pipe, full-screen TUI apps (vim, htop) won't work and some tools detect the
// missing TTY and disable colors; the actual use case — run commands next to
// the agent session and attach their output to prompts — works. M3 added a
// real PTY where util-linux script(1) is available (see IsPty); everywhere
// else, including Windows and macOS, the pipe remains.

public sealed class TerminalSession : IDisposable
{
    private readonly Process _shell;
    private readonly ConcurrentDictionary<Guid, Channel<SessionEventDto>> _subscribers = new();
    private readonly StringBuilder _buffer = new();
    private readonly object _bufferLock = new();
    private const int BufferCap = 64_000;

    /// <summary>
    /// True when the shell runs inside a real pseudo-terminal. This is
    /// achieved with util-linux `script -qfe -c bash /dev/null`, which
    /// allocates a PTY and bridges it to our pipes: isatty() is true inside,
    /// so interactive prompts, colors and line editing work, and the PTY
    /// echoes input (the UI must NOT locally echo in this mode). Without
    /// util-linux script — Windows, macOS, BusyBox — the plain pipe is used
    /// (ConPTY interop is a deliberate non-goal for zero dependencies).
    /// </summary>
    public bool IsPty { get; }

    public string Id { get; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// util-linux's script(1), or null. PTY mode passes util-linux options
    /// (-q -f -e -c); the `script` of macOS and BusyBox rejects them, and the
    /// shell would die on start. So the binary must identify itself as
    /// util-linux before it is trusted. Probed once per process.
    /// </summary>
    internal static readonly Lazy<string?> UtilLinuxScript = new(FindUtilLinuxScript);

    internal static bool IsUtilLinux(string versionOutput) =>
        versionOutput.Contains("util-linux", StringComparison.OrdinalIgnoreCase);

    private static string? FindUtilLinuxScript()
    {
        if (OperatingSystem.IsWindows()) return null;
        foreach (var path in new[] { "/usr/bin/script", "/bin/script" })
        {
            if (!File.Exists(path)) continue;
            try
            {
                using var probe = Process.Start(new ProcessStartInfo(path, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                });
                if (probe is null) continue;
                var stdout = probe.StandardOutput.ReadToEndAsync();
                var stderr = probe.StandardError.ReadToEndAsync();
                if (!probe.WaitForExit(3000)) { probe.Kill(entireProcessTree: true); continue; }
                if (IsUtilLinux(stdout.Result + stderr.Result)) return path;
            }
            catch { /* not runnable here — try the next one */ }
        }
        return null;
    }

    public TerminalSession(string cwd)
    {
        var scriptPath = UtilLinuxScript.Value;
        IsPty = scriptPath is not null;

        var psi = new ProcessStartInfo
        {
            WorkingDirectory = cwd,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (IsPty)
        {
            psi.FileName = scriptPath!;
            foreach (var a in new[] { "-qfe", "-c", "/bin/bash", "/dev/null" })
                psi.ArgumentList.Add(a);
            psi.Environment["TERM"] = "xterm-256color";
        }
        else
        {
            psi.FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash";
        }

        _shell = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start shell.");

        _ = PumpAsync(_shell.StandardOutput);
        _ = PumpAsync(_shell.StandardError);
    }

    public bool HasExited => _shell.HasExited;

    private async Task PumpAsync(StreamReader reader)
    {
        var chunk = new char[2048];
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(chunk, 0, chunk.Length);
                if (read <= 0) break;
                var text = new string(chunk, 0, read);
                lock (_bufferLock)
                {
                    _buffer.Append(text);
                    if (_buffer.Length > BufferCap)
                        _buffer.Remove(0, _buffer.Length - BufferCap);
                }
                foreach (var (_, channel) in _subscribers)
                    channel.Writer.TryWrite(new SessionEventDto("out", text, null));
            }
        }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    }

    public async Task WriteInputAsync(string line, CancellationToken ct)
    {
        await _shell.StandardInput.WriteLineAsync(line.AsMemory(), ct);
        await _shell.StandardInput.FlushAsync(ct);
    }

    public string BufferSnapshot()
    {
        lock (_bufferLock) return _buffer.ToString();
    }

    public ChannelReader<SessionEventDto> Subscribe(out Guid token)
    {
        token = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<SessionEventDto>();
        _subscribers[token] = channel;
        return channel.Reader;
    }

    public void Unsubscribe(Guid token)
    {
        if (_subscribers.TryRemove(token, out var channel))
            channel.Writer.TryComplete();
    }

    public void Dispose()
    {
        foreach (var (_, channel) in _subscribers) channel.Writer.TryComplete();
        _subscribers.Clear();
        try { if (!_shell.HasExited) _shell.Kill(entireProcessTree: true); }
        catch { /* already gone */ }
        _shell.Dispose();
    }
}

/// <summary>Any number of terminals per Helm session, in creation order; all are torn down with it.</summary>
public sealed class TerminalManager : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<TerminalSession>> _bySession = new();

    public TerminalSession Create(string sessionId, string cwd)
    {
        var terminal = new TerminalSession(cwd);
        lock (_lock)
        {
            if (!_bySession.TryGetValue(sessionId, out var terminals))
            {
                terminals = new List<TerminalSession>();
                _bySession[sessionId] = terminals;
            }
            terminals.Add(terminal);
        }
        return terminal;
    }

    public TerminalSession? Get(string sessionId, string terminalId)
    {
        lock (_lock)
            return _bySession.TryGetValue(sessionId, out var terminals)
                ? terminals.Find(t => t.Id == terminalId)
                : null;
    }

    public IReadOnlyList<TerminalSession> List(string sessionId)
    {
        lock (_lock)
            return _bySession.TryGetValue(sessionId, out var terminals) ? terminals.ToList() : [];
    }

    public bool Close(string sessionId, string terminalId)
    {
        TerminalSession? terminal;
        lock (_lock)
        {
            if (!_bySession.TryGetValue(sessionId, out var terminals)) return false;
            terminal = terminals.Find(t => t.Id == terminalId);
            if (terminal is null) return false;
            terminals.Remove(terminal);
            if (terminals.Count == 0) _bySession.Remove(sessionId);
        }
        terminal.Dispose();
        return true;
    }

    public void Remove(string sessionId)
    {
        lock (_lock)
        {
            if (!_bySession.Remove(sessionId, out var terminals)) return;
            foreach (var terminal in terminals) terminal.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var terminal in _bySession.Values.SelectMany(t => t)) terminal.Dispose();
            _bySession.Clear();
        }
    }
}
