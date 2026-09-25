using System.Text.Json;

namespace ClaudeStatus.Core;

public enum Status
{
    /// <summary>Claude finished its turn and waits for the next prompt.</summary>
    Idle,
    /// <summary>Claude (or an agent) is working.</summary>
    Working,
    /// <summary>Claude asks something: permission prompt, AskUserQuestion, plan approval.</summary>
    Awaiting,
    /// <summary>Agent finished.</summary>
    Done,
}

/// <summary>One line of a session's event log as written by claude-status-hook.</summary>
public sealed record HookEvent
{
    public DateTimeOffset Time { get; init; }
    public string Event { get; init; } = "";
    public string SessionId { get; init; } = "";
    public string? Cwd { get; init; }
    public string? TranscriptPath { get; init; }
    public string? AgentId { get; init; }
    public string? AgentType { get; init; }
    public string? ToolName { get; init; }
    public string? ToolUseId { get; init; }
    public string? NotificationType { get; init; }
    public string? Message { get; init; }
    public string? Prompt { get; init; }
    public string? Source { get; init; }
    public string? Description { get; init; }
    public string? SubagentType { get; init; }
    public bool RunInBackground { get; init; }

    public static HookEvent? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            var ev = S(r, "event") ?? S(r, "hook_event_name");
            var sid = S(r, "session_id");
            if (ev == null || sid == null) return null;
            long ts = r.TryGetProperty("ts", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt64() : 0;
            return new HookEvent
            {
                Time = ts > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ts) : DateTimeOffset.UtcNow,
                Event = ev,
                SessionId = sid,
                Cwd = S(r, "cwd"),
                TranscriptPath = S(r, "transcript_path"),
                AgentId = S(r, "agent_id"),
                AgentType = S(r, "agent_type"),
                ToolName = S(r, "tool_name"),
                ToolUseId = S(r, "tool_use_id"),
                NotificationType = S(r, "notification_type"),
                Message = S(r, "message"),
                Prompt = S(r, "prompt"),
                Source = S(r, "source"),
                Description = S(r, "description"),
                SubagentType = S(r, "subagent_type"),
                RunInBackground = r.TryGetProperty("run_in_background", out var bg) && bg.ValueKind == JsonValueKind.True,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? S(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}

public sealed class AgentState
{
    /// <summary>Stable key for the UI (tool_use_id of the launch, or agent_id).</summary>
    public required string Key { get; init; }
    public string? AgentId { get; set; }
    public string? ToolUseId { get; set; }
    public string Type { get; set; } = "agent";
    public string? Description { get; set; }
    public bool Background { get; set; }
    public Status Status { get; set; } = Status.Working;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset StatusSince { get; set; }
    public DateTimeOffset LastActivity { get; set; }
    public string? LastTool { get; set; }

    public string Title => string.IsNullOrWhiteSpace(Description) ? Type : Description!;
}

public sealed class SessionState
{
    private static readonly HashSet<string> AgentTools = new(StringComparer.OrdinalIgnoreCase) { "Task", "Agent" };
    private static readonly HashSet<string> QuestionTools = new(StringComparer.OrdinalIgnoreCase) { "AskUserQuestion", "ExitPlanMode" };

    public required string SessionId { get; init; }
    public string? Cwd { get; private set; }
    public string? LastPrompt { get; private set; }
    public string? LastTool { get; private set; }
    public string? AwaitingReason { get; private set; }
    public Status Status { get; private set; } = Status.Idle;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset StatusSince { get; private set; }
    public DateTimeOffset LastEventAt { get; private set; }
    public bool Ended { get; private set; }
    public List<AgentState> Agents { get; } = new();

    // Who put the session into Awaiting (null = main thread) and what it was doing before.
    private string? _awaitingAgentKey;
    private Status _statusBeforeAwaiting = Status.Working;

    public string ProjectName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Cwd)) return "Session " + ShortId;
            var trimmed = Cwd!.TrimEnd('\\', '/');
            var idx = trimmed.LastIndexOfAny(new[] { '\\', '/' });
            var name = idx >= 0 ? trimmed[(idx + 1)..] : trimmed;
            return string.IsNullOrEmpty(name) ? trimmed : name;
        }
    }

    public string ShortId => SessionId.Length > 8 ? SessionId[..8] : SessionId;

    public void Apply(HookEvent e)
    {
        if (StartedAt == default) StartedAt = e.Time;
        if (e.Time > LastEventAt) LastEventAt = e.Time;

        bool fromAgent = e.AgentId != null && e.Event is not ("SubagentStart" or "SubagentStop");
        if (!fromAgent && e.Cwd != null) Cwd = e.Cwd;

        switch (e.Event)
        {
            case "SessionStart":
                Ended = false;
                if (e.Source != "compact") SetStatus(Status.Idle, e.Time);
                break;

            case "SessionEnd":
                Ended = true;
                SetStatus(Status.Idle, e.Time);
                break;

            case "UserPromptSubmit":
                if (e.Prompt != null) LastPrompt = e.Prompt;
                SetStatus(Status.Working, e.Time);
                break;

            case "PreToolUse":
                if (fromAgent) { AgentActivity(e, Status.Working); break; }
                LastTool = e.ToolName;
                if (e.ToolName != null && AgentTools.Contains(e.ToolName)) AddLaunch(e);
                if (e.ToolName != null && QuestionTools.Contains(e.ToolName))
                    EnterAwaiting(null, e.ToolName == "ExitPlanMode" ? "Plan freigeben" : "Rückfrage", e.Time);
                else
                    SetStatus(Status.Working, e.Time);
                break;

            case "PostToolUse":
            case "PostToolUseFailure":
                if (fromAgent) { AgentActivity(e, Status.Working); break; }
                if (e.ToolName != null && AgentTools.Contains(e.ToolName)) FinishLaunch(e);
                SetStatus(Status.Working, e.Time);
                break;

            case "PermissionRequest":
                if (fromAgent) { var a = AgentActivity(e, Status.Awaiting); EnterAwaiting(a?.Key, "Berechtigung", e.Time); }
                else EnterAwaiting(null, "Berechtigung", e.Time);
                break;

            case "Notification":
                if (IsQuestionNotification(e))
                {
                    AgentState? a = fromAgent ? AgentActivity(e, Status.Awaiting) : null;
                    EnterAwaiting(a?.Key, e.NotificationType is "elicitation_dialog" or "elicitation_url_dialog" or "agent_needs_input" ? "Rückfrage" : "Berechtigung", e.Time);
                }
                break;

            case "Elicitation":
                EnterAwaiting(null, "Rückfrage", e.Time);
                break;

            case "ElicitationResult":
            case "PermissionDenied":
                if (Status == Status.Awaiting) LeaveAwaiting(e.Time);
                break;

            case "PreCompact":
                if (!fromAgent) SetStatus(Status.Working, e.Time);
                break;

            case "Stop":
            case "StopFailure":
                if (fromAgent) break;
                SetStatus(Status.Idle, e.Time);
                // Foreground agents can't outlive the turn that started them.
                foreach (var a in Agents)
                    if (!a.Background && a.Status != Status.Done) SetAgentStatus(a, Status.Done, e.Time);
                break;

            case "SubagentStart":
                BindAgent(e);
                break;

            case "SubagentStop":
                if (e.AgentId != null && FindAgent(e.AgentId) is { } done)
                {
                    done.LastActivity = e.Time;
                    SetAgentStatus(done, Status.Done, e.Time);
                    if (_awaitingAgentKey == done.Key) LeaveAwaiting(e.Time);
                }
                break;
        }
    }

    private static bool IsQuestionNotification(HookEvent e)
    {
        switch (e.NotificationType)
        {
            case "permission_prompt":
            case "elicitation_dialog":
            case "elicitation_url_dialog":
            case "agent_needs_input":
                return true;
            case null:
                // Older Claude Code versions don't send notification_type.
                return e.Message != null && e.Message.Contains("permission", StringComparison.OrdinalIgnoreCase);
            default:
                return false; // idle_prompt, auth_success, ...
        }
    }

    private void SetStatus(Status s, DateTimeOffset t)
    {
        if (s != Status.Awaiting) { _awaitingAgentKey = null; AwaitingReason = null; }
        if (Status == s) return;
        Status = s;
        StatusSince = t;
    }

    private void EnterAwaiting(string? agentKey, string reason, DateTimeOffset t)
    {
        if (Status != Status.Awaiting) _statusBeforeAwaiting = Status;
        _awaitingAgentKey = agentKey;
        AwaitingReason = reason;
        if (Status != Status.Awaiting) { Status = Status.Awaiting; StatusSince = t; }
    }

    private void LeaveAwaiting(DateTimeOffset t) => SetStatus(_statusBeforeAwaiting == Status.Awaiting ? Status.Working : _statusBeforeAwaiting, t);

    private static void SetAgentStatus(AgentState a, Status s, DateTimeOffset t)
    {
        if (a.Status == s) return;
        a.Status = s;
        a.StatusSince = t;
    }

    private AgentState? FindAgent(string agentId) => Agents.FirstOrDefault(a => a.AgentId == agentId);

    private void AddLaunch(HookEvent e)
    {
        var key = e.ToolUseId ?? ("launch-" + e.Time.ToUnixTimeMilliseconds());
        if (Agents.Any(a => a.Key == key)) return;
        Agents.Add(new AgentState
        {
            Key = key,
            ToolUseId = e.ToolUseId,
            Type = e.SubagentType ?? "general-purpose",
            Description = e.Description,
            Background = e.RunInBackground,
            Status = Status.Working,
            StartedAt = e.Time,
            StatusSince = e.Time,
            LastActivity = e.Time,
        });
    }

    private void FinishLaunch(HookEvent e)
    {
        var a = e.ToolUseId != null ? Agents.FirstOrDefault(x => x.ToolUseId == e.ToolUseId) : null;
        if (a == null) return;
        a.LastActivity = e.Time;
        // A background agent's tool call returns right away; it ends with SubagentStop.
        if (!a.Background) SetAgentStatus(a, Status.Done, e.Time);
    }

    /// <summary>Connects an agent_id to the Task/Agent tool call that launched it.</summary>
    private AgentState BindAgent(HookEvent e)
    {
        var id = e.AgentId!;
        if (FindAgent(id) is { } existing) return existing;
        var type = e.AgentType;
        var launch =
            Agents.FirstOrDefault(a => a.AgentId == null && a.Status != Status.Done && type != null &&
                                       string.Equals(a.Type, type, StringComparison.OrdinalIgnoreCase)) ??
            Agents.FirstOrDefault(a => a.AgentId == null && a.Status != Status.Done);
        if (launch == null)
        {
            launch = new AgentState
            {
                Key = id,
                Type = type ?? "agent",
                StartedAt = e.Time,
                StatusSince = e.Time,
            };
            Agents.Add(launch);
        }
        launch.AgentId = id;
        if (type != null && launch.Type == "general-purpose") launch.Type = type;
        launch.LastActivity = e.Time;
        SetAgentStatus(launch, Status.Working, e.Time);
        return launch;
    }

    private AgentState? AgentActivity(HookEvent e, Status s)
    {
        if (e.AgentId == null) return null;
        var a = BindAgent(e);
        a.LastActivity = e.Time;
        if (e.ToolName != null) a.LastTool = e.ToolName;
        SetAgentStatus(a, s, e.Time);
        // The agent moves on after its permission prompt was answered.
        if (s == Status.Working && Status == Status.Awaiting && _awaitingAgentKey == a.Key) LeaveAwaiting(e.Time);
        return a;
    }

    /// <summary>Drops finished or silent agents so the tile doesn't fill up.</summary>
    public bool PruneAgents(DateTimeOffset now, TimeSpan keepDone, TimeSpan maxSilence)
    {
        int removed = Agents.RemoveAll(a =>
            (a.Status == Status.Done && now - a.StatusSince > keepDone) ||
            (a.Status != Status.Done && now - a.LastActivity > maxSilence));
        return removed > 0;
    }
}
