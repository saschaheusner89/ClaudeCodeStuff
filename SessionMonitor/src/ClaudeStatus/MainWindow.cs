using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeStatus.Core;
using ClaudeStatus.Shared;

namespace ClaudeStatus;

public sealed class MainWindow : Window
{
    private readonly SessionStore _store = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly AdaptivePanel _tiles = new() { PreferredAspect = 1.6, Gap = 6 };
    private readonly Dictionary<string, SessionTile> _byId = new();
    private readonly StackPanel _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _installButton = new() { Content = "Hooks in Claude Code installieren", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 12, 0, 0) };
    private readonly MenuItem _topmostItem = new() { Header = "Immer im Vordergrund (T)", IsCheckable = true };
    private DateTime _lastFullRefresh;

    public MainWindow()
    {
        Title = "Claude Sessions";
        Background = new SolidColorBrush(Palette.WindowBackground);
        MinWidth = 120;
        MinHeight = 60;
        Width = _settings.Width;
        Height = _settings.Height;
        if (_settings.Left is { } l && _settings.Top is { } t && IsOnScreen(l, t))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = l;
            Top = t;
        }
        Topmost = _settings.Topmost;
        _topmostItem.IsChecked = Topmost;

        var emptyText = new TextBlock
        {
            Text = "Keine aktive Claude-Code-Session",
            Foreground = new SolidColorBrush(Palette.TextDim),
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        };
        _empty.Children.Add(emptyText);
        _empty.Children.Add(_installButton);
        _installButton.Click += (_, _) => { ShowInfo(Program.InstallHooks()); UpdateEmptyState(recheckHooks: true); };

        var root = new Grid { Margin = new Thickness(6) };
        root.Children.Add(_tiles);
        root.Children.Add(_empty);
        Content = root;

        ContextMenu = BuildWindowMenu();
        KeyDown += OnKeyDown;
        SourceInitialized += (_, _) => UseDarkTitleBar();
        Closing += (_, _) => SaveSettings();

        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Tick();
        UpdateEmptyState(recheckHooks: true);
    }

    private void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        bool changed;
        try { changed = _store.Poll(now); }
        catch (Exception) { changed = false; }

        // Durations tick every second even without new events.
        if (!changed && DateTime.UtcNow - _lastFullRefresh < TimeSpan.FromSeconds(1)) return;
        _lastFullRefresh = DateTime.UtcNow;

        var visible = _store.Visible(now);
        var ids = new HashSet<string>(visible.Select(s => s.SessionId));
        foreach (var gone in _byId.Keys.Where(k => !ids.Contains(k)).ToList())
        {
            _tiles.Children.Remove(_byId[gone]);
            _byId.Remove(gone);
        }
        for (int i = 0; i < visible.Count; i++)
        {
            var s = visible[i];
            if (!_byId.TryGetValue(s.SessionId, out var tile))
            {
                tile = CreateTile(s.SessionId);
                _byId[s.SessionId] = tile;
            }
            int at = _tiles.Children.IndexOf(tile);
            if (at != i)
            {
                if (at >= 0) _tiles.Children.RemoveAt(at);
                _tiles.Children.Insert(i, tile);
            }
            tile.Update(s, now);
        }

        int working = visible.Count(s => s.Status == Status.Working);
        int awaiting = visible.Count(s => s.Status == Status.Awaiting);
        Title = awaiting > 0 ? $"⚠ {awaiting} wartet · Claude Sessions"
            : working > 0 ? $"{working} aktiv · Claude Sessions"
            : "Claude Sessions";
        UpdateEmptyState();
    }

    private void UpdateEmptyState(bool recheckHooks = false)
    {
        _empty.Visibility = _tiles.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (recheckHooks)
            _installButton.Visibility = HookInstaller.IsInstalled(HookInstaller.DefaultSettingsPath) ? Visibility.Collapsed : Visibility.Visible;
    }

    private SessionTile CreateTile(string id)
    {
        var tile = new SessionTile(id);
        tile.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!SessionLauncher.FocusClaudeDesktop())
                ShowInfo("Die Claude-Desktop-App wurde nicht gefunden.\nRechtsklick → „Im Terminal fortsetzen“ öffnet die Session in der CLI.");
        };

        var menu = new ContextMenu();
        menu.Items.Add(Item("Claude-Desktop-App in den Vordergrund", () => SessionLauncher.FocusClaudeDesktop()));
        menu.Items.Add(Item("Im Terminal fortsetzen (claude --resume)", () => { if (tile.State != null) SessionLauncher.ResumeInTerminal(tile.State); }));
        menu.Items.Add(Item("Projektordner öffnen", () => { if (tile.State != null) SessionLauncher.OpenFolder(tile.State); }));
        menu.Items.Add(Item("Session-ID kopieren", () => Clipboard.SetText(id)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Aus Anzeige entfernen", () => { _store.Remove(id); _lastFullRefresh = default; Tick(); }));
        tile.ContextMenu = menu;
        return tile;
    }

    private ContextMenu BuildWindowMenu()
    {
        var menu = new ContextMenu();
        _topmostItem.Click += (_, _) => SetTopmost(_topmostItem.IsChecked);
        menu.Items.Add(_topmostItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Hooks installieren / aktualisieren", () => { ShowInfo(Program.InstallHooks()); UpdateEmptyState(recheckHooks: true); }));
        menu.Items.Add(Item("Hooks entfernen", () => { ShowInfo(Program.UninstallHooks()); UpdateEmptyState(recheckHooks: true); }));
        menu.Items.Add(Item("Datenordner öffnen", () =>
        {
            System.IO.Directory.CreateDirectory(Paths.SessionsDir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.DataDir}\"") { UseShellExecute = true });
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Beenden", Close));
        return menu;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { ShowInfo(ex.Message); }
        };
        return item;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.T) SetTopmost(!Topmost);
        else if (e.Key == Key.Escape) WindowState = WindowState.Minimized;
    }

    private void SetTopmost(bool on)
    {
        Topmost = on;
        _topmostItem.IsChecked = on;
        SaveSettings();
    }

    private void SaveSettings()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.Left = bounds.Left;
        _settings.Top = bounds.Top;
        _settings.Width = bounds.Width;
        _settings.Height = bounds.Height;
        _settings.Topmost = Topmost;
        _settings.Save();
    }

    private static bool IsOnScreen(double left, double top) =>
        left >= SystemParameters.VirtualScreenLeft - 50 &&
        top >= SystemParameters.VirtualScreenTop - 50 &&
        left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50 &&
        top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50;

    private static void ShowInfo(string text) =>
        MessageBox.Show(text, "Claude Status", MessageBoxButton.OK, MessageBoxImage.Information);

    private void UseDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int on = 1;
            DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
        }
        catch (Exception) { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
