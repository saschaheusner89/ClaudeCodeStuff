using ClaudeStatus.Core;
using Xunit;

namespace ClaudeStatus.Tests;

public class SessionStateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private int _sec;

    private HookEvent E(string ev, string? tool = null, string? agentId = null, string? agentType = null,
        string? toolUseId = null, string? notification = null, string? subagentType = null,
        string? description = null, bool background = false) => new()
    {
        Time = T0.AddSeconds(++_sec),
        Event = ev,
        SessionId = "s1",
        Cwd = @"C:\code\myproject",
        ToolName = tool,
        AgentId = agentId,
        AgentType = agentType,
        ToolUseId = toolUseId,
        NotificationType = notification,
        SubagentType = subagentType,
        Description = description,
        RunInBackground = background,
    };

    private static SessionState New() => new() { SessionId = "s1" };

    [Fact]
    public void Basic_turn_goes_idle_working_idle()
    {
        var s = New();
        s.Apply(E("SessionStart"));
        Assert.Equal(Status.Idle, s.Status);
        s.Apply(E("UserPromptSubmit") with { Prompt = "fix the bug" });
        Assert.Equal(Status.Working, s.Status);
        Assert.Equal("fix the bug", s.LastPrompt);
        s.Apply(E("PreToolUse", "Bash"));
        s.Apply(E("PostToolUse", "Bash"));
        Assert.Equal(Status.Working, s.Status);
        s.Apply(E("Stop"));
        Assert.Equal(Status.Idle, s.Status);
        Assert.Equal("myproject", s.ProjectName);
    }

    [Fact]
    public void Permission_prompt_is_awaiting_until_tool_runs()
    {
        var s = New();
        s.Apply(E("UserPromptSubmit"));
        s.Apply(E("PreToolUse", "Bash"));
        s.Apply(E("PermissionRequest", "Bash"));
        Assert.Equal(Status.Awaiting, s.Status);
        s.Apply(E("Notification", notification: "permission_prompt"));
        Assert.Equal(Status.Awaiting, s.Status);
        s.Apply(E("PostToolUse", "Bash"));
        Assert.Equal(Status.Working, s.Status);
    }

    [Fact]
    public void Idle_prompt_notification_does_not_mean_awaiting()
    {
        var s = New();
        s.Apply(E("UserPromptSubmit"));
        s.Apply(E("Stop"));
        s.Apply(E("Notification", notification: "idle_prompt"));
        Assert.Equal(Status.Idle, s.Status);
    }

    [Fact]
    public void AskUserQuestion_is_awaiting()
    {
        var s = New();
        s.Apply(E("UserPromptSubmit"));
        s.Apply(E("PreToolUse", "AskUserQuestion"));
        Assert.Equal(Status.Awaiting, s.Status);
        s.Apply(E("PostToolUse", "AskUserQuestion"));
        Assert.Equal(Status.Working, s.Status);
    }

    [Fact]
    public void Foreground_agent_lifecycle()
    {
        var s = New();
        s.Apply(E("UserPromptSubmit"));
        s.Apply(E("PreToolUse", "Agent", toolUseId: "tu1", subagentType: "Explore", description: "Find auth code"));
        var a = Assert.Single(s.Agents);
        Assert.Equal("Explore", a.Type);
        Assert.Equal("Find auth code", a.Title);
        Assert.Equal(Status.Working, a.Status);

        s.Apply(E("SubagentStart", agentId: "ag1", agentType: "Explore"));
        s.Apply(E("PreToolUse", "Grep", agentId: "ag1", agentType: "Explore"));
        Assert.Single(s.Agents);
        Assert.Equal("ag1", a.AgentId);
        Assert.Equal("Grep", a.LastTool);
        Assert.Equal("Agent", s.LastTool); // subagent tools do not overwrite the main thread
        s.Apply(E("SubagentStop", agentId: "ag1", agentType: "Explore"));
        Assert.Equal(Status.Done, a.Status);
        s.Apply(E("PostToolUse", "Agent", toolUseId: "tu1"));
        Assert.Equal(Status.Done, a.Status);
    }

    [Fact]
    public void Parallel_agents_bind_by_type()
    {
        var s = New();
        s.Apply(E("UserPromptSubmit"));
        s.Apply(E("PreToolUse", "Agent", toolUseId: "tu1", subagentType: "Explore", description: "A"));
        s.Apply(E("PreToolUse", "Agent", toolUseId: "tu2", subagentType: "Plan", description: "B"));
        s.Apply(E("SubagentStart", agentId: "p", agentType: "Plan"));
        s.Apply(E("SubagentStart", agentId: "x", agentType: "Explore"));
        Assert.Equal(2, s.Agents.Count);
        Assert.Equal("x", s.Agents.Single(a => a.Description == "A").AgentId);
        Assert.Equal("p", s.Agents.Single(a => a.Description == "B").AgentId);
    }

    [Fact]
    public void Agent_permission_prompt_makes_session_awaiting_and_resumes()
    {
        var s = New();
        s.Apply(E("UserPromptSubmit"));
        s.Apply(E("PreToolUse", "Agent", toolUseId: "tu1", subagentType: "general-purpose"));
        s.Apply(E("SubagentStart", agentId: "g", agentType: "general-purpose"));
        s.Apply(E("PermissionRequest", "Bash", agentId: "g", agentType: "general-purpose"));
        Assert.Equal(Status.Awaiting, s.Status);
        Assert.Equal(Status.Awaiting, s.Agents[0].Status);
        s.Apply(E("PostToolUse", "Bash", agentId: "g", agentType: "general-purpose"));
        Assert.Equal(Status.Working, s.Status);
        Assert.Equal(Status.Working, s.Agents[0].Status);
    }

    [Fact]
    public void Background_agent_survives_stop_and_ends_on_subagent_stop()
    {
        var s = New();
        s.Apply(E("UserPromptSubmit"));
        s.Apply(E("PreToolUse", "Agent", toolUseId: "tu1", subagentType: "general-purpose", background: true));
        s.Apply(E("PostToolUse", "Agent", toolUseId: "tu1"));
        s.Apply(E("SubagentStart", agentId: "bg", agentType: "general-purpose"));
        s.Apply(E("Stop"));
        Assert.Equal(Status.Idle, s.Status);
        Assert.Equal(Status.Working, s.Agents[0].Status);
        s.Apply(E("PreToolUse", "Read", agentId: "bg"));
        Assert.Equal(Status.Idle, s.Status); // background work doesn't flip the main status
        s.Apply(E("SubagentStop", agentId: "bg"));
        Assert.Equal(Status.Done, s.Agents[0].Status);
    }

    [Fact]
    public void Foreground_agents_are_closed_by_stop()
    {
        var s = New();
        s.Apply(E("UserPromptSubmit"));
        s.Apply(E("PreToolUse", "Task", toolUseId: "tu1", subagentType: "Explore"));
        s.Apply(E("Stop"));
        Assert.Equal(Status.Done, s.Agents[0].Status);
    }

    [Fact]
    public void Prune_removes_old_done_agents()
    {
        var s = New();
        s.Apply(E("PreToolUse", "Agent", toolUseId: "tu1"));
        s.Apply(E("PostToolUse", "Agent", toolUseId: "tu1"));
        Assert.False(s.PruneAgents(T0.AddSeconds(10), TimeSpan.FromSeconds(45), TimeSpan.FromMinutes(30)));
        Assert.True(s.PruneAgents(T0.AddSeconds(100), TimeSpan.FromSeconds(45), TimeSpan.FromMinutes(30)));
        Assert.Empty(s.Agents);
    }

    [Fact]
    public void SessionEnd_marks_ended()
    {
        var s = New();
        s.Apply(E("SessionStart"));
        s.Apply(E("SessionEnd"));
        Assert.True(s.Ended);
    }
}
