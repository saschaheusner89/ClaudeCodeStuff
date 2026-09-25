using System.IO;
using System.Windows;
using ClaudeStatus.Core;

namespace ClaudeStatus;

public static class Program
{
    public static string HookExePath => Path.Combine(AppContext.BaseDirectory, "claude-status-hook.exe");

    [STAThread]
    public static int Main(string[] args)
    {
        var arg = args.FirstOrDefault()?.ToLowerInvariant();
        if (arg is "--install" or "--uninstall")
        {
            var msg = arg == "--install" ? InstallHooks() : UninstallHooks();
            MessageBox.Show(msg, "Claude Status", MessageBoxButton.OK, MessageBoxImage.Information);
            return 0;
        }

        // Only one monitor window at a time.
        using var single = new Mutex(true, @"Local\ClaudeStatus_Viewer", out bool first);
        if (!first) return 0;

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        return app.Run(new MainWindow());
    }

    public static string InstallHooks()
    {
        if (!File.Exists(HookExePath))
            return $"claude-status-hook.exe wurde nicht gefunden.\nErwartet: {HookExePath}\n\nBitte beide .exe-Dateien im selben Ordner lassen.";
        try
        {
            var backup = HookInstaller.Install(HookInstaller.DefaultSettingsPath, HookExePath);
            return "Hooks eingetragen in\n" + HookInstaller.DefaultSettingsPath +
                   (backup != null ? "\n\nSicherung: " + backup : "") +
                   "\n\nNeue Claude-Code-Sessions werden ab jetzt angezeigt. Laufende Sessions ggf. neu starten." +
                   "\n\nWichtig: Den Ordner mit den .exe-Dateien nicht mehr verschieben (sonst erneut installieren).";
        }
        catch (Exception ex)
        {
            return "Installation fehlgeschlagen:\n" + ex.Message;
        }
    }

    public static string UninstallHooks()
    {
        try
        {
            var backup = HookInstaller.Uninstall(HookInstaller.DefaultSettingsPath);
            return "Hooks entfernt." + (backup != null ? "\n\nSicherung: " + backup : "");
        }
        catch (Exception ex)
        {
            return "Entfernen fehlgeschlagen:\n" + ex.Message;
        }
    }
}
