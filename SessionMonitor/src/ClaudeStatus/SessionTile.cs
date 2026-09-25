using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClaudeStatus.Core;

namespace ClaudeStatus;

/// <summary>One session: colored tile with project name, status and a mini window per running agent.</summary>
public sealed class SessionTile : Border
{
    /// <summary>A working session that stays silent this long is shown as "no activity".</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    private readonly PulseBrush _bg = new();
    private readonly PulseBrush _outline = new(outline: true);
    private readonly Grid _grid = new();
    private readonly StackPanel _header = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new() { FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _status = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _prompt = new() { TextTrimming = TextTrimming.CharacterEllipsis, FontStyle = FontStyles.Italic, Opacity = 0.75 };
    private readonly AdaptivePanel _agents = new() { PreferredAspect = 2.6, Gap = 4 };
    private readonly Dictionary<string, AgentChip> _chips = new();
    private bool? _sideBySide;

    public string SessionId { get; }
    public SessionState? State { get; private set; }

    public SessionTile(string sessionId)
    {
        SessionId = sessionId;
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(2);
        Background = _bg.Brush;
        BorderBrush = _outline.Brush;
        Padding = new Thickness(10, 8, 10, 8);
        Cursor = System.Windows.Input.Cursors.Hand;
        ClipToBounds = true;

        _header.Children.Add(_title);
        _header.Children.Add(_status);
        _header.Children.Add(_prompt);
        _grid.Children.Add(_header);
        _grid.Children.Add(_agents);
        Child = _grid;
        SizeChanged += (_, _) => Relayout();
    }

    public void Update(SessionState s, DateTimeOffset now)
    {
        State = s;
        bool stale = s.Status == Status.Working && now - s.LastEventAt > StaleAfter;
        var look = stale ? Palette.Stale : s.Status switch
        {
            Status.Working => Palette.Working,
            Status.Awaiting => Palette.Awaiting,
            _ => Palette.Idle,
        };
        _bg.Apply(look);
        _outline.Apply(look);
        var fg = new SolidColorBrush(look.Text);
        _title.Foreground = _status.Foreground = _prompt.Foreground = fg;

        _title.Text = s.ProjectName;
        var since = Fmt.Duration(now - s.StatusSince);
        _status.Text = stale
            ? $"keine Aktivität seit {Fmt.Duration(now - s.LastEventAt)}"
            : s.Status switch
            {
                Status.Working => $"arbeitet · {since}" + (s.LastTool != null ? $" · {s.LastTool}" : ""),
                Status.Awaiting => $"wartet auf dich · {s.AwaitingReason ?? "Rückfrage"} · {since}",
                _ => $"bereit · {since}",
            };
        _prompt.Text = s.LastPrompt != null ? "„" + OneLine(s.LastPrompt) + "“" : "";
        ToolTip = $"{s.ProjectName}\n{s.Cwd}\nSession {s.SessionId}\n\nKlick: Claude öffnen · Rechtsklick: mehr";

        // Agent mini windows.
        var keys = new HashSet<string>(s.Agents.Select(a => a.Key));
        foreach (var gone in _chips.Keys.Where(k => !keys.Contains(k)).ToList())
        {
            _agents.Children.Remove(_chips[gone]);
            _chips.Remove(gone);
        }
        foreach (var a in s.Agents)
        {
            if (!_chips.TryGetValue(a.Key, out var chip))
            {
                chip = new AgentChip();
                _chips[a.Key] = chip;
                _agents.Children.Add(chip);
            }
            chip.Update(a, now);
        }
        Relayout();
    }

    private static string OneLine(string text)
    {
        var t = text.ReplaceLineEndings(" ").Trim();
        return t.Length > 160 ? t[..160] + "…" : t;
    }

    /// <summary>
    /// Wide tiles put the agents to the right of the header, tall tiles put them below.
    /// Font sizes follow the tile size.
    /// </summary>
    private void Relayout()
    {
        double w = Math.Max(0, ActualWidth - Padding.Left - Padding.Right);
        double h = Math.Max(0, ActualHeight - Padding.Top - Padding.Bottom);
        bool hasAgents = _chips.Count > 0;
        bool sideBySide = w > h * 2.2;

        double baseSize = Math.Clamp(Math.Min(w / (sideBySide && hasAgents ? 22 : 14), h / 5), 9, 22);
        _title.FontSize = baseSize * 1.35;
        _status.FontSize = baseSize;
        _prompt.FontSize = baseSize * 0.9;
        double lineH = baseSize * 1.4;
        _status.Visibility = h > lineH * 1.9 ? Visibility.Visible : Visibility.Collapsed;

        if (_sideBySide != sideBySide || _grid.RowDefinitions.Count + _grid.ColumnDefinitions.Count == 0)
        {
            _sideBySide = sideBySide;
            _grid.RowDefinitions.Clear();
            _grid.ColumnDefinitions.Clear();
            if (sideBySide)
            {
                _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
                Grid.SetRow(_agents, 0); Grid.SetColumn(_agents, 1);
                _agents.Margin = new Thickness(8, 0, 0, 0);
            }
            else
            {
                _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(_agents, 1); Grid.SetColumn(_agents, 0);
                _agents.Margin = new Thickness(0, 6, 0, 0);
            }
        }

        if (!hasAgents)
        {
            // Without agents the header gets all the room.
            _agents.Visibility = Visibility.Collapsed;
            Grid.SetRowSpan(_header, sideBySide ? 1 : 2);
            Grid.SetColumnSpan(_header, sideBySide ? 2 : 1);
            _prompt.Visibility = h > lineH * 3.2 && _prompt.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _prompt.TextWrapping = TextWrapping.Wrap;
            _prompt.MaxHeight = Math.Max(lineH, h - lineH * 2.6);
        }
        else
        {
            _agents.Visibility = Visibility.Visible;
            Grid.SetRowSpan(_header, 1);
            Grid.SetColumnSpan(_header, 1);
            _prompt.TextWrapping = TextWrapping.NoWrap;
            _prompt.MaxHeight = double.PositiveInfinity;
            _prompt.Visibility = (sideBySide ? h > lineH * 3.2 : h > lineH * 7) && _prompt.Text.Length > 0
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
