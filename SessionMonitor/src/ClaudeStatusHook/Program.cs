using System.Text;
using System.Text.Json;
using ClaudeStatus.Shared;

// claude-status-hook
// Called by Claude Code for every configured hook event. Reads the event JSON from stdin
// and appends a compact line to %LOCALAPPDATA%\ClaudeStatus\sessions\<session_id>.jsonl.
// It must never block or disturb Claude Code: no stdout output, always exit code 0.

try
{
    Run();
}
catch
{
    // Swallow everything: a broken monitor must never break a Claude Code session.
}
return 0;

static void Run()
{
    if (!Console.IsInputRedirected) return;

    string input;
    using (var stdin = Console.OpenStandardInput())
    using (var reader = new StreamReader(stdin, new UTF8Encoding(false)))
        input = reader.ReadToEnd();
    if (string.IsNullOrWhiteSpace(input)) return;

    using var doc = JsonDocument.Parse(input);
    var root = doc.RootElement;
    if (root.ValueKind != JsonValueKind.Object) return;

    var sessionId = Str(root, "session_id");
    var eventName = Str(root, "hook_event_name");
    if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(eventName)) return;

    var buffer = new MemoryStream();
    using (var w = new Utf8JsonWriter(buffer))
    {
        w.WriteStartObject();
        w.WriteNumber("ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        w.WriteString("event", eventName);
        w.WriteString("session_id", sessionId);
        Copy(w, root, "cwd");
        Copy(w, root, "transcript_path");
        Copy(w, root, "permission_mode");
        Copy(w, root, "agent_id");
        Copy(w, root, "agent_type");
        Copy(w, root, "tool_name");
        Copy(w, root, "tool_use_id");
        Copy(w, root, "notification_type");
        Copy(w, root, "message", 300);
        Copy(w, root, "prompt", 300);
        Copy(w, root, "source");
        Copy(w, root, "reason");

        if (root.TryGetProperty("tool_input", out var ti) && ti.ValueKind == JsonValueKind.Object)
        {
            Copy(w, ti, "description", 200);
            Copy(w, ti, "subagent_type");
            if (ti.TryGetProperty("run_in_background", out var bg) &&
                (bg.ValueKind == JsonValueKind.True || bg.ValueKind == JsonValueKind.False))
                w.WriteBoolean("run_in_background", bg.GetBoolean());
        }
        w.WriteEndObject();
    }
    buffer.WriteByte((byte)'\n');

    Directory.CreateDirectory(Paths.SessionsDir);
    var file = Path.Combine(Paths.SessionsDir, Paths.SafeFileName(sessionId) + ".jsonl");

    // Hooks of parallel subagents run concurrently: serialize appends per session.
    using var mutex = new Mutex(false, @"Local\ClaudeStatus_" + Paths.SafeFileName(sessionId));
    bool owned = false;
    try
    {
        try { owned = mutex.WaitOne(2000); }
        catch (AbandonedMutexException) { owned = true; }

        using var fs = new FileStream(file, FileMode.Append, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        buffer.Position = 0;
        buffer.CopyTo(fs);
    }
    finally
    {
        if (owned) mutex.ReleaseMutex();
    }
}

static string? Str(JsonElement obj, string name) =>
    obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

static void Copy(Utf8JsonWriter w, JsonElement obj, string name, int maxLen = 1000)
{
    var s = Str(obj, name);
    if (string.IsNullOrEmpty(s)) return;
    if (s.Length > maxLen) s = s[..maxLen] + "…";
    w.WriteString(name, s);
}
