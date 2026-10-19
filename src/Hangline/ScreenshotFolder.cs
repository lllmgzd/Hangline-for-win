using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Hangline;

/// 接管系统的截图保存位置。
///
/// Windows 把截图存在"Screenshots"这个已知文件夹里（默认在图片目录下）。
/// 这里把它的注册表指向晾绳自己的目录，系统截图就直接落进来，桌面和图片目录都不会堆东西。
/// 原值会记下来，关掉开关或者退出程序时原样还回去。
internal static class ScreenshotFolder
{
    /// 从 HKLM\...\Explorer\FolderDescriptions 里读到的那一项。
    private const string KnownFolderId = "{b7bede81-df94-4682-a7d8-57a52620b86f}";

    private const string KeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";

    /// 原来没有这一项时用这个标记，还原时把键删掉就等于回到系统默认。
    private const string Absent = "<not-set>";

    private static string BackupPath => Path.Combine(AppSettings.DataFolder, "screenshots-folder-backup.txt");

    public static bool TakeOver(string folder)
    {
        try
        {
            System.IO.Directory.CreateDirectory(folder);
            SaveOriginalOnce();

            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (key is null) return false;

            key.SetValue(KnownFolderId, folder, RegistryValueKind.ExpandString);
            Broadcast();
            Log.Note($"screenshot folder -> {folder}");
            return true;
        }
        catch (Exception exception)
        {
            Log.Note("take over failed", exception);
            return false;
        }
    }

    public static void Restore()
    {
        try
        {
            if (!File.Exists(BackupPath)) return;

            var original = File.ReadAllText(BackupPath).Trim();
            using (var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true))
            {
                if (key is not null)
                {
                    if (original.Length == 0 || original == Absent)
                    {
                        key.DeleteValue(KnownFolderId, throwOnMissingValue: false);
                    }
                    else
                    {
                        key.SetValue(KnownFolderId, original, RegistryValueKind.ExpandString);
                    }
                }
            }

            File.Delete(BackupPath);
            Broadcast();
            Log.Note("screenshot folder restored");
        }
        catch (Exception exception)
        {
            Log.Note("restore failed", exception);
        }
    }

    /// 当前值。按原样读，不展开 %USERPROFILE% 这类变量，方便之后原样写回。
    private static string CurrentValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            var value = key?.GetValue(
                KnownFolderId, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            return string.IsNullOrEmpty(value) ? Absent : value!;
        }
        catch
        {
            return Absent;
        }
    }

    /// 只在第一次接管时记原值。之后重复接管不再覆盖，
    /// 免得把我们已经改过的路径当成"系统原来的"。
    private static void SaveOriginalOnce()
    {
        if (File.Exists(BackupPath)) return;
        File.WriteAllText(BackupPath, CurrentValue());
    }

    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    /// 告诉资源管理器和截图工具位置变了。
    private static void Broadcast()
    {
        try
        {
            SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "Shell", SMTO_ABORTIFHUNG, 3000, out _);
        }
        catch
        {
            // 广播不出去也不致命，注销重登之后一样生效。
        }
    }
}
