using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ClaudeStatus.Core;

namespace ClaudeStatus;

/// <summary>Ways to jump from a tile to the session.</summary>
public static class SessionLauncher
{
    /// <summary>
    /// Brings the Claude desktop app to the front. There is no documented way to open a specific
    /// session in the desktop app, so this only focuses the window.
    /// </summary>
    public static bool FocusClaudeDesktop()
    {
        foreach (var name in new[] { "Claude", "claude" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    var hwnd = p.MainWindowHandle;
                    if (hwnd == IntPtr.Zero) continue;
                    if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
                    SetForegroundWindow(hwnd);
                    return true;
                }
                catch (InvalidOperationException) { }
                finally { p.Dispose(); }
            }
        }
        return false;
    }

    /// <summary>Opens a terminal in the project folder and resumes the session with the Claude Code CLI.</summary>
    public static void ResumeInTerminal(SessionState s)
    {
        var cwd = s.Cwd != null && Directory.Exists(s.Cwd) ? s.Cwd : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try
        {
            var psi = new ProcessStartInfo("wt.exe") { UseShellExecute = true };
            psi.ArgumentList.Add("-d");
            psi.ArgumentList.Add(cwd);
            psi.ArgumentList.Add("claude");
            psi.ArgumentList.Add("--resume");
            psi.ArgumentList.Add(s.SessionId);
            Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No Windows Terminal: fall back to a classic console.
            var psi = new ProcessStartInfo("cmd.exe") { WorkingDirectory = cwd, UseShellExecute = true };
            psi.ArgumentList.Add("/k");
            psi.ArgumentList.Add($"claude --resume {s.SessionId}");
            Process.Start(psi);
        }
    }

    public static void OpenFolder(SessionState s)
    {
        if (s.Cwd != null && Directory.Exists(s.Cwd))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{s.Cwd}\"") { UseShellExecute = true });
    }

    private const int SW_RESTORE = 9;
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
}
