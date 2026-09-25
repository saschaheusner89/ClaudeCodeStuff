using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeStatus.Core;

/// <summary>Adds/removes the claude-status-hook entries in Claude Code's user settings.json.</summary>
public static class HookInstaller
{
    public const string Marker = "claude-status-hook";

    /// <summary>Events we listen to. Tool events get the "*" matcher.</summary>
    public static readonly string[] Events =
    {
        "SessionStart", "SessionEnd", "UserPromptSubmit",
        "PreToolUse", "PostToolUse", "PostToolUseFailure",
        "PermissionRequest", "PermissionDenied", "Notification",
        "Stop", "StopFailure", "SubagentStart", "SubagentStop", "PreCompact",
    };

    private static readonly HashSet<string> ToolEvents = new()
    {
        "PreToolUse", "PostToolUse", "PostToolUseFailure", "PermissionRequest", "PermissionDenied",
    };

    public static string DefaultSettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static bool IsInstalled(string settingsPath)
    {
        if (!File.Exists(settingsPath)) return false;
        try { return File.ReadAllText(settingsPath).Contains(Marker, StringComparison.OrdinalIgnoreCase); }
        catch (IOException) { return false; }
    }

    /// <summary>Returns the path of the backup that was written (or null if there was no file yet).</summary>
    public static string? Install(string settingsPath, string hookExePath)
    {
        var root = Load(settingsPath);
        var hooks = root["hooks"] as JsonObject;
        if (hooks == null) { hooks = new JsonObject(); root["hooks"] = hooks; }

        RemoveOurs(hooks);
        foreach (var ev in Events)
        {
            if (hooks[ev] is not JsonArray groups) { groups = new JsonArray(); hooks[ev] = groups; }
            var group = new JsonObject();
            if (ToolEvents.Contains(ev)) group["matcher"] = "*";
            group["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                // Exec form (args present): Claude Code starts the exe directly, no shell quoting issues.
                ["command"] = hookExePath.Replace('\\', '/'),
                ["args"] = new JsonArray(),
                ["timeout"] = 10,
            });
            groups.Add(group);
        }
        return Save(settingsPath, root);
    }

    public static string? Uninstall(string settingsPath)
    {
        if (!File.Exists(settingsPath)) return null;
        var root = Load(settingsPath);
        if (root["hooks"] is JsonObject hooks)
        {
            RemoveOurs(hooks);
            if (hooks.Count == 0) root.Remove("hooks");
        }
        return Save(settingsPath, root);
    }

    private static void RemoveOurs(JsonObject hooks)
    {
        foreach (var (ev, node) in hooks.ToList())
        {
            if (node is not JsonArray groups) continue;
            foreach (var group in groups.OfType<JsonObject>().ToList())
            {
                if (group["hooks"] is not JsonArray list) continue;
                foreach (var h in list.OfType<JsonObject>().ToList())
                    if (h["command"]?.ToString().Contains(Marker, StringComparison.OrdinalIgnoreCase) == true)
                        list.Remove(h);
                if (list.Count == 0) groups.Remove(group);
            }
            if (groups.Count == 0) hooks.Remove(ev);
        }
    }

    private static JsonObject Load(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        return node as JsonObject ?? throw new InvalidDataException($"{path} enthält kein JSON-Objekt.");
    }

    private static string? Save(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string? backup = null;
        if (File.Exists(path))
        {
            backup = path + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(path, backup, overwrite: true);
        }
        var json = root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json + Environment.NewLine);
        File.Move(tmp, path, overwrite: true);
        return backup;
    }
}
