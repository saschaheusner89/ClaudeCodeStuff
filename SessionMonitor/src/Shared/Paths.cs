namespace ClaudeStatus.Shared;

/// <summary>Locations shared by the hook and the viewer.</summary>
public static class Paths
{
    public static string DataDir
    {
        get
        {
            var overrideDir = Environment.GetEnvironmentVariable("CLAUDE_STATUS_DIR");
            if (!string.IsNullOrWhiteSpace(overrideDir)) return overrideDir;
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local))
                local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(local, "ClaudeStatus");
        }
    }

    public static string SessionsDir => Path.Combine(DataDir, "sessions");

    /// <summary>Only keep characters that are safe in a file name.</summary>
    public static string SafeFileName(string id)
    {
        Span<char> buf = stackalloc char[Math.Min(id.Length, 128)];
        int n = 0;
        foreach (var c in id)
        {
            if (n == buf.Length) break;
            if (char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_') buf[n++] = c;
        }
        return n == 0 ? "unknown" : new string(buf[..n]);
    }
}
