using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ClaudeStatus;

/// <summary>What a tile looks like in a given state.</summary>
public sealed record Look(string Key, Color Base, Color Peak, double PulseSeconds, Color Text)
{
    public bool Pulses => PulseSeconds > 0;
}

public static class Palette
{
    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static readonly Color WindowBackground = C("#15171B");
    public static readonly Color TextBright = C("#F2F4F7");
    public static readonly Color TextDim = C("#AEB4BE");

    // Working: calm blue breathing.
    public static readonly Look Working = new("working", C("#163659"), C("#2C74CC"), 2.8, TextBright);
    // Awaiting input: amber, faster and brighter. Only used for real questions/permission prompts.
    public static readonly Look Awaiting = new("awaiting", C("#6E4300"), C("#F5A623"), 1.3, TextBright);
    // Idle: steady muted green.
    public static readonly Look Idle = new("idle", C("#1C3F2C"), C("#1C3F2C"), 0, TextBright);
    // Working but nothing heard for a long time (crashed or very long command).
    public static readonly Look Stale = new("stale", C("#30333A"), C("#30333A"), 0, TextDim);
    // Finished agent.
    public static readonly Look Done = new("done", C("#243128"), C("#243128"), 0, TextDim);

    // Agent chips sit on top of the tile, so they are a little lighter.
    public static readonly Look AgentWorking = new("a-working", C("#1F4A7A"), C("#4A95F0"), 2.0, TextBright);
    public static readonly Look AgentAwaiting = Awaiting with { Key = "a-awaiting" };
}

/// <summary>A brush that can breathe between two colors.</summary>
public sealed class PulseBrush
{
    private readonly bool _outline;
    private string? _key;

    /// <param name="outline">Outline brushes use the peak color, fading between faint and full.</param>
    public PulseBrush(bool outline = false) => _outline = outline;

    public SolidColorBrush Brush { get; } = new(Colors.Transparent);

    public void Apply(Look look)
    {
        if (_key == look.Key) return;
        _key = look.Key;

        var from = _outline ? WithAlpha(look.Peak, look.Pulses ? (byte)0x30 : (byte)0x00) : look.Base;
        var to = _outline ? WithAlpha(look.Peak, 0xFF) : look.Peak;

        Brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        Brush.Color = from;
        if (!look.Pulses) return;
        Brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(look.PulseSeconds / 2),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
}
