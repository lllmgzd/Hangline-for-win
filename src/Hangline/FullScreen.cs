using System;
using System.Windows.Forms;

namespace Hangline;

/// 前台窗口是不是把整块屏幕占满了（全屏游戏、全屏视频、投屏）。
internal static class FullScreen
{
    /// 桌面和任务栏这些外壳不算全屏。
    private static readonly string[] ShellClasses =
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    public static bool IsActive(IntPtr ownWindow)
    {
        try
        {
            var foreground = Native.GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == ownWindow) return false;
            if (!Native.IsWindowVisible(foreground)) return false;

            var className = Native.ClassNameOf(foreground);
            foreach (var shell in ShellClasses)
            {
                if (string.Equals(className, shell, StringComparison.Ordinal)) return false;
            }

            if (!Native.GetWindowRect(foreground, out var rect)) return false;

            var screen = Screen.FromHandle(foreground);
            if (screen is null) return false;

            // 最大化窗口下面留着任务栏，盖不满；真全屏才盖满整块屏。
            var bounds = screen.Bounds;
            return rect.Left <= bounds.Left
                && rect.Top <= bounds.Top
                && rect.Right >= bounds.Right
                && rect.Bottom >= bounds.Bottom;
        }
        catch
        {
            return false;
        }
    }
}
