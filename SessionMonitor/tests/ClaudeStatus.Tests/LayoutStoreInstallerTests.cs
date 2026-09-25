using System.Text.Json.Nodes;
using ClaudeStatus.Core;
using Xunit;

namespace ClaudeStatus.Tests;

public class GridLayoutTests
{
    [Theory]
    [InlineData(4, 1600, 150, 4, 1)]  // wide strip -> one row
    [InlineData(4, 200, 1000, 1, 4)]  // tall strip -> one column
    [InlineData(4, 800, 500, 2, 2)]   // normal window -> 2x2
    [InlineData(1, 800, 500, 1, 1)]
    [InlineData(3, 900, 300, 3, 1)]
    [InlineData(6, 1200, 500, 3, 2)]
    public void Grid_follows_window_shape(int n, double w, double h, int cols, int rows)
    {
        Assert.Equal((cols, rows), GridLayout.ChooseGrid(n, w, h, 1.6));
    }

    [Fact]
    public void Last_row_is_stretched_to_full_width()
    {
        var cells = GridLayout.Arrange(3, 800, 500, 1.6, 0);
        Assert.Equal(3, cells.Count);
        Assert.Equal(400, cells[0].Width, 3);
        Assert.Equal(800, cells[2].Width, 3);
    }
}

public class SessionStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cs-test-" + Guid.NewGuid().ToString("N"));

    public SessionStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static long Ms(DateTimeOffset t) => t.ToUnixTimeMilliseconds();

    [Fact]
    public void Tails_files_incrementally_and_ignores_partial_lines()
    {
        var now = DateTimeOffset.UtcNow;
        var file = Path.Combine(_dir, "abc.jsonl");
        File.WriteAllText(file,
            $"{{\"ts\":{Ms(now)},\"event\":\"SessionStart\",\"session_id\":\"abc\",\"cwd\":\"/x/proj\"}}\n" +
            $"{{\"ts\":{Ms(now)},\"event\":\"UserPromptSubmit\",\"session_id\":\"abc\"");  // incomplete line

        var store = new SessionStore(_dir);
        Assert.True(store.Poll(now));
        var s = Assert.Single(store.Visible(now));
        Assert.Equal(Status.Idle, s.Status);
        Assert.Equal("proj", s.ProjectName);

        File.AppendAllText(file, ",\"prompt\":\"hi\"}\n");
        Assert.True(store.Poll(now));
        Assert.Equal(Status.Working, store.Visible(now)[0].Status);
        Assert.False(store.Poll(now));
    }

    [Fact]
    public void SessionEnd_removes_session_and_file()
    {
        var now = DateTimeOffset.UtcNow;
        var file = Path.Combine(_dir, "abc.jsonl");
        File.WriteAllText(file,
            $"{{\"ts\":{Ms(now)},\"event\":\"SessionStart\",\"session_id\":\"abc\"}}\n" +
            $"{{\"ts\":{Ms(now)},\"event\":\"SessionEnd\",\"session_id\":\"abc\"}}\n");
        var store = new SessionStore(_dir);
        store.Poll(now);
        Assert.Empty(store.Visible(now));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Old_idle_sessions_are_hidden()
    {
        var now = DateTimeOffset.UtcNow;
        var old = now.AddHours(-9);
        File.WriteAllText(Path.Combine(_dir, "a.jsonl"),
            $"{{\"ts\":{Ms(old)},\"event\":\"Stop\",\"session_id\":\"a\"}}\n");
        var store = new SessionStore(_dir);
        store.Poll(now);
        Assert.Empty(store.Visible(now));
    }
}

public class HookInstallerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cs-inst-" + Guid.NewGuid().ToString("N"));
    private string Settings => Path.Combine(_dir, "settings.json");

    public HookInstallerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Install_keeps_existing_settings_and_is_idempotent()
    {
        File.WriteAllText(Settings, """
        {
          // user comment
          "model": "opus",
          "hooks": {
            "Stop": [ { "hooks": [ { "type": "command", "command": "say done" } ] } ]
          },
        }
        """);
        var backup = HookInstaller.Install(Settings, @"C:\Tools\ClaudeStatus\claude-status-hook.exe");
        Assert.NotNull(backup);
        HookInstaller.Install(Settings, @"C:\Tools\ClaudeStatus\claude-status-hook.exe");

        var root = JsonNode.Parse(File.ReadAllText(Settings))!.AsObject();
        Assert.Equal("opus", root["model"]!.ToString());
        var stop = root["hooks"]!["Stop"]!.AsArray();
        Assert.Equal(2, stop.Count); // user's hook + ours, not duplicated
        Assert.Equal("say done", stop[0]!["hooks"]![0]!["command"]!.ToString());
        Assert.Equal("C:/Tools/ClaudeStatus/claude-status-hook.exe", stop[1]!["hooks"]![0]!["command"]!.ToString());
        Assert.Equal("*", root["hooks"]!["PreToolUse"]![0]!["matcher"]!.ToString());
        Assert.True(HookInstaller.IsInstalled(Settings));

        HookInstaller.Uninstall(Settings);
        root = JsonNode.Parse(File.ReadAllText(Settings))!.AsObject();
        Assert.Single(root["hooks"]!["Stop"]!.AsArray());
        Assert.Null(root["hooks"]!["PreToolUse"]);
        Assert.False(HookInstaller.IsInstalled(Settings));
    }

    [Fact]
    public void Install_creates_settings_when_missing()
    {
        Assert.Null(HookInstaller.Install(Settings, "/opt/claude-status-hook.exe"));
        Assert.True(HookInstaller.IsInstalled(Settings));
    }
}
