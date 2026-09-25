using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClaudeStatus.Core;

namespace ClaudeStatus;

/// <summary>The small window inside a session tile that shows one subagent.</summary>
public sealed class AgentChip : Border
{
    private readonly PulseBrush _bg = new();
    private readonly PulseBrush _outline = new(outline: true);
    private readonly TextBlock _type = new() { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _desc = new() { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap };

    public AgentChip()
    {
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1.5);
        Background = _bg.Brush;
        BorderBrush = _outline.Brush;
        Padding = new Thickness(6, 3, 6, 3);
        ClipToBounds = true;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(_type);
        stack.Children.Add(_desc);
        Child = stack;
        SizeChanged += (_, _) => Relayout();
    }

    public void Update(AgentState a, DateTimeOffset now)
    {
        var look = a.Status switch
        {
            Status.Awaiting => Palette.AgentAwaiting,
            Status.Done => Palette.Done,
            _ => Palette.AgentWorking,
        };
        _bg.Apply(look);
        _outline.Apply(look);
        _type.Foreground = _desc.Foreground = new SolidColorBrush(look.Text);
        Opacity = a.Status == Status.Done ? 0.65 : 1.0;

        var state = a.Status switch
        {
            Status.Awaiting => "wartet auf dich",
            Status.Done => "fertig",
            _ => a.LastTool != null ? a.LastTool : "arbeitet",
        };
        _type.Text = a.Type + (a.Background ? " ⟲" : "") + " · " + state;
        _desc.Text = a.Description ?? "";
        _desc.Visibility = string.IsNullOrEmpty(a.Description) ? Visibility.Collapsed : Visibility.Visible;
        ToolTip = $"{a.Type}{(a.Background ? " (Hintergrund)" : "")}\n{a.Description}\n{state} · seit {Fmt.Duration(now - a.StartedAt)}";
        Relayout();
    }

    private void Relayout()
    {
        double h = ActualHeight, w = ActualWidth;
        double size = Math.Clamp(Math.Min(h / 2.6, w / 11), 8, 14);
        _type.FontSize = size;
        _desc.FontSize = size * 0.92;
        _desc.MaxHeight = Math.Max(0, h - size * 2);
        _desc.Visibility = h < size * 3 || string.IsNullOrEmpty(_desc.Text) ? Visibility.Collapsed : Visibility.Visible;
    }
}

internal static class Fmt
{
    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalSeconds < 60) return $"{(int)t.TotalSeconds}s";
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        if (t.TotalHours < 24) return $"{(int)t.TotalHours}h {t.Minutes:00}m";
        return $"{(int)t.TotalDays}d";
    }
}
