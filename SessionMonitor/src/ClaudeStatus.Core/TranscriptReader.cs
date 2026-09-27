using System.Text;
using System.Text.Json;

namespace ClaudeStatus.Core;

/// <summary>
/// Incrementally reads a Claude Code transcript (.jsonl) for the session title and the last
/// text Claude wrote. Unknown line types are ignored, so format changes degrade gracefully.
/// </summary>
public sealed class TranscriptReader
{
    /// <summary>Initial read covers at most this much of the end of a large transcript.</summary>
    private const long InitialTailBytes = 4 * 1024 * 1024;

    private long _offset = -1;

    public string Path { get; }
    /// <summary>Name given with /rename or claude -n.</summary>
    public string? CustomTitle { get; private set; }
    /// <summary>Automatically generated title/summary.</summary>
    public string? AutoTitle { get; private set; }
    public string? LastAssistantText { get; private set; }

    public string? Title => CustomTitle ?? AutoTitle;

    public TranscriptReader(string path) => Path = path;

    /// <summary>Reads new lines; returns true if title or text changed.</summary>
    public bool Poll()
    {
        FileInfo info;
        try { info = new FileInfo(Path); if (!info.Exists) return false; }
        catch (Exception) { return false; }

        long length = info.Length;
        if (_offset > length) _offset = -1; // rewritten
        if (_offset == length) return false;

        var before = (CustomTitle, AutoTitle, LastAssistantText);
        try
        {
            using var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (_offset < 0)
            {
                // First look: titles are usually near the start, the last message near the end.
                if (length > InitialTailBytes)
                {
                    ReadRange(fs, 0, Math.Min(256 * 1024, length), titlesOnly: true);
                    _offset = SkipToLineStart(fs, length - InitialTailBytes);
                }
                else _offset = 0;
            }
            _offset = ReadRange(fs, _offset, length, titlesOnly: false);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }

        return before != (CustomTitle, AutoTitle, LastAssistantText);
    }

    private static long SkipToLineStart(FileStream fs, long pos)
    {
        fs.Seek(pos, SeekOrigin.Begin);
        int b;
        while ((b = fs.ReadByte()) != -1)
        {
            pos++;
            if (b == '\n') break;
        }
        return pos;
    }

    /// <summary>Parses complete lines in [start, end) and returns the offset after the last complete line.</summary>
    private long ReadRange(FileStream fs, long start, long end, bool titlesOnly)
    {
        fs.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[end - start];
        int read = 0;
        while (read < buffer.Length)
        {
            int n = fs.Read(buffer, read, buffer.Length - read);
            if (n == 0) break;
            read += n;
        }
        int last = Array.LastIndexOf(buffer, (byte)'\n', Math.Max(0, read - 1));
        if (last < 0) return start;

        int lineStart = 0;
        for (int i = 0; i <= last; i++)
        {
            if (buffer[i] != '\n') continue;
            if (i > lineStart) ParseLine(new ReadOnlySpan<byte>(buffer, lineStart, i - lineStart), titlesOnly);
            lineStart = i + 1;
        }
        return start + last + 1;
    }

    public void ParseLine(ReadOnlySpan<byte> line, bool titlesOnly = false)
    {
        // Cheap pre-filter: only parse lines that can matter.
        bool maybeTitle = line.IndexOf("itle\""u8) >= 0 || line.IndexOf("\"summary\""u8) >= 0;
        bool maybeAssistant = !titlesOnly && line.IndexOf("\"assistant\""u8) >= 0;
        if (!maybeTitle && !maybeAssistant) return;

        try
        {
            var reader = new Utf8JsonReader(line);
            using var doc = JsonDocument.ParseValue(ref reader);
            var o = doc.RootElement;
            if (o.ValueKind != JsonValueKind.Object) return;
            var type = Str(o, "type");

            switch (type)
            {
                case "custom-title":
                    CustomTitle = Clean(Str(o, "customTitle") ?? Str(o, "title")) ?? CustomTitle;
                    return;
                case "summary":
                    AutoTitle = Clean(Str(o, "summary")) ?? AutoTitle;
                    return;
                case "assistant" when !titlesOnly:
                    if (o.TryGetProperty("isSidechain", out var sc) && sc.ValueKind == JsonValueKind.True) return;
                    if (o.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object &&
                        msg.TryGetProperty("content", out var content))
                    {
                        var text = ExtractText(content);
                        if (!string.IsNullOrWhiteSpace(text)) LastAssistantText = text;
                    }
                    return;
            }

            // Other title-like entries (e.g. "ai-title" / "session-title"): take the first *title field.
            if (type != null && type.Contains("title", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var p in o.EnumerateObject())
                {
                    if (p.Name == "type" || p.Value.ValueKind != JsonValueKind.String) continue;
                    if (p.Name.EndsWith("itle", StringComparison.Ordinal))
                    {
                        AutoTitle = Clean(p.Value.GetString()) ?? AutoTitle;
                        return;
                    }
                }
            }
        }
        catch (JsonException) { }
    }

    private static string? ExtractText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String) return content.GetString();
        if (content.ValueKind != JsonValueKind.Array) return null;
        var sb = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object && Str(block, "type") == "text" && Str(block, "text") is { } t)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(t);
            }
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.ReplaceLineEndings(" ").Trim();
        return s.Length > 120 ? s[..120] + "…" : s;
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
