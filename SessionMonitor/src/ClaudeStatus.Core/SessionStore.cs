using System.Text;
using ClaudeStatus.Shared;

namespace ClaudeStatus.Core;

/// <summary>
/// Tails the per-session event logs written by the hook and folds them into <see cref="SessionState"/>s.
/// Polling (instead of FileSystemWatcher) keeps it simple and never misses an event.
/// </summary>
public sealed class SessionStore
{
    private sealed class Tail
    {
        public long Offset;
        public DateTime LastWrite;
        public required SessionState State;
    }

    private readonly string _dir;
    private readonly Dictionary<string, Tail> _tails = new(StringComparer.OrdinalIgnoreCase);

    public TimeSpan KeepDoneAgents { get; set; } = TimeSpan.FromSeconds(45);
    public TimeSpan MaxAgentSilence { get; set; } = TimeSpan.FromMinutes(30);
    /// <summary>Idle sessions without any event for this long are hidden (Claude Code doesn't always send SessionEnd).</summary>
    public TimeSpan HideIdleAfter { get; set; } = TimeSpan.FromHours(8);
    /// <summary>Log files untouched for this long are deleted.</summary>
    public TimeSpan DeleteLogsAfter { get; set; } = TimeSpan.FromDays(2);

    public SessionStore(string? dir = null) => _dir = dir ?? Paths.SessionsDir;

    public string Directory => _dir;

    /// <summary>Sessions to show, oldest first so tiles keep their position.</summary>
    public IReadOnlyList<SessionState> Visible(DateTimeOffset now) =>
        _tails.Values.Select(t => t.State)
            .Where(s => !s.Ended && !(s.Status == Status.Idle && now - s.LastEventAt > HideIdleAfter))
            .OrderBy(s => s.StartedAt)
            .ThenBy(s => s.SessionId, StringComparer.Ordinal)
            .ToList();

    /// <summary>Reads new events. Returns true if anything changed.</summary>
    public bool Poll(DateTimeOffset now)
    {
        bool changed = false;
        System.IO.Directory.CreateDirectory(_dir);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in System.IO.Directory.EnumerateFiles(_dir, "*.jsonl"))
        {
            seen.Add(file);
            FileInfo info;
            try { info = new FileInfo(file); info.Refresh(); }
            catch (IOException) { continue; }

            if (!_tails.TryGetValue(file, out var tail))
            {
                if (now.UtcDateTime - info.LastWriteTimeUtc > DeleteLogsAfter)
                {
                    TryDelete(file);
                    continue;
                }
                tail = new Tail { State = new SessionState { SessionId = Path.GetFileNameWithoutExtension(file) } };
                _tails[file] = tail;
                changed = true;
            }

            if (info.Length < tail.Offset)
            {
                // File was recreated: start over.
                tail.Offset = 0;
                tail.State = new SessionState { SessionId = tail.State.SessionId };
                changed = true;
            }

            if (info.Length > tail.Offset)
            {
                changed |= ReadNew(file, tail);
                tail.LastWrite = info.LastWriteTimeUtc;
            }

            if (tail.State.Ended)
            {
                TryDelete(file);
                _tails.Remove(file);
                changed = true;
                continue;
            }

            changed |= tail.State.PruneAgents(now, KeepDoneAgents, MaxAgentSilence);
            if (tail.State.Transcript is { } transcript) changed |= transcript.Poll();
        }

        foreach (var gone in _tails.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _tails.Remove(gone);
            changed = true;
        }
        return changed;
    }

    private static bool ReadNew(string file, Tail tail)
    {
        byte[] bytes;
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fs.Seek(tail.Offset, SeekOrigin.Begin);
            bytes = new byte[fs.Length - tail.Offset];
            int read = 0;
            while (read < bytes.Length)
            {
                int n = fs.Read(bytes, read, bytes.Length - read);
                if (n == 0) break;
                read += n;
            }
            if (read < bytes.Length) Array.Resize(ref bytes, read);
        }
        catch (IOException)
        {
            return false;
        }

        // Only consume complete lines; a hook may be mid-write.
        int end = Array.LastIndexOf(bytes, (byte)'\n');
        if (end < 0) return false;
        tail.Offset += end + 1;

        bool any = false;
        foreach (var line in Encoding.UTF8.GetString(bytes, 0, end).Split('\n'))
        {
            var ev = HookEvent.Parse(line);
            if (ev == null) continue;
            tail.State.Apply(ev);
            any = true;
        }
        return any;
    }

    /// <summary>Forget a session (e.g. one that was closed without SessionEnd). It reappears on its next event.</summary>
    public void Remove(string sessionId)
    {
        foreach (var (file, tail) in _tails.Where(kv => kv.Value.State.SessionId == sessionId).ToList())
        {
            TryDelete(file);
            _tails.Remove(file);
        }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
