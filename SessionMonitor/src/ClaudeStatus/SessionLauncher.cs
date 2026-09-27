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
        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName("Claude"))
        {
            pids.Add((uint)p.Id);
            p.Dispose();
        }

        // Electron apps run many processes; the window may belong to any of them and
        // Process.MainWindowHandle is often zero. Search all top-level windows instead.
        IntPtr best = IntPtr.Zero;
        if (pids.Count > 0)
        {
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (!pids.Contains(pid) || GetWindowTextLength(hwnd) == 0) return true;
                if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return true;
                if (IsWindowVisible(hwnd) || IsIconic(hwnd))
                {
                    best = hwnd;
                    return false;
                }
                if (best == IntPtr.Zero) best = hwnd; // hidden (e.g. closed to tray) - keep looking
                return true;
            }, IntPtr.Zero);
        }

        if (best != IntPtr.Zero)
        {
            ShowWindow(best, IsIconic(best) || !IsWindowVisible(best) ? SW_RESTORE : SW_SHOW);
            // Windows only lets the foreground app hand over focus; a synthetic ALT press unlocks it.
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            SetForegroundWindow(best);
            return true;
        }

        // Not running or no window found: let the protocol handler start/focus the app.
        try
        {
            Process.Start(new ProcessStartInfo("claude://") { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
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

    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;
    private const uint GW_OWNER = 4;
    private const byte VK_MENU = 0x12;
    private const uint KEYEVENTF_KEYUP = 0x2;
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
}
