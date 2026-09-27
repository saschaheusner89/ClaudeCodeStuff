using ClaudeStatus.Core;
using Xunit;

namespace ClaudeStatus.Tests;

public class TranscriptReaderTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "tr-" + Guid.NewGuid().ToString("N") + ".jsonl");
    public void Dispose() { try { File.Delete(_file); } catch { } }

    private const string Assistant = """{"type":"assistant","isSidechain":false,"message":{"role":"assistant","content":[{"type":"thinking","thinking":"hmm"},{"type":"text","text":"Build ist grün."}]}}""";
    private const string SideAssistant = """{"type":"assistant","isSidechain":true,"message":{"content":[{"type":"text","text":"agent text"}]}}""";
    private const string ToolOnly = """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash","input":{}}]}}""";

    [Fact]
    public void Reads_last_main_assistant_text_and_titles_incrementally()
    {
        File.WriteAllText(_file, """{"type":"user","message":{"content":"hi"}}""" + "\n" + Assistant + "\n" + SideAssistant + "\n" + ToolOnly + "\n");
        var r = new TranscriptReader(_file);
        Assert.True(r.Poll());
        Assert.Equal("Build ist grün.", r.LastAssistantText);
        Assert.Null(r.Title);

        File.AppendAllText(_file, """{"type":"summary","summary":"Session monitor bauen","leafUuid":"x"}""" + "\n");
        Assert.True(r.Poll());
        Assert.Equal("Session monitor bauen", r.Title);

        File.AppendAllText(_file, """{"type":"custom-title","customTitle":"Monitor","sessionId":"s"}""" + "\n");
        r.Poll();
        Assert.Equal("Monitor", r.Title);

        File.AppendAllText(_file, """{"type":"ai-title","aiTitle":"Other"}""" + "\n");
        r.Poll();
        Assert.Equal("Monitor", r.Title); // custom name wins over automatic titles
        Assert.Equal("Other", r.AutoTitle);

        Assert.False(r.Poll());
    }

    [Fact]
    public void Missing_file_is_harmless()
    {
        Assert.False(new TranscriptReader(_file).Poll());
    }
}
