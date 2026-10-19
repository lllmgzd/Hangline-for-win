using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Hangline;

internal static class Program
{
    private static Mutex? _instance;

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Note("unhandled", e.ExceptionObject as Exception ?? new Exception("未知异常"));
        try
        {
            Run();
        }
        catch (Exception exception)
        {
            Log.Note("startup failed", exception);
            throw;
        }
    }

    private static void Run()
    {
        _instance = new Mutex(true, @"Local\Hangline.SingleInstance", out var isFirst);
        if (!isFirst) return;

        var settings = AppSettings.Load();
        L.Use(settings.Language);
        Log.Note($"starting, language={L.Current}");

        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Note("dispatcher", e.Exception);
            e.Handled = true;
        };

        // 绳子先建出来并显示：内容缩在屏幕外，但仍然收消息。
        var line = new LineWindow(settings);
        line.Show();

        var messages = new MessageWindow();

        // 接管系统截图目录的话，系统截图直接落到我们自己的目录里。
        if (settings.TakeOverScreenshotFolder)
        {
            ScreenshotFolder.TakeOver(AppSettings.InboxFolder);
        }
        else
        {
            // 上次接管过但没还回去（被强杀之类），这里补一次。
            ScreenshotFolder.Restore();
        }

        ScreenshotWatcher? watcher = null;

        // 自己目录里的图（截的、接的）取下绳子时可以连带删掉，用户自己的目录不动。
        static bool IsOurs(string path) =>
            path.StartsWith(AppSettings.DataFolder, StringComparison.OrdinalIgnoreCase);

        void ApplyWatchFolder()
        {
            watcher?.Dispose();

            var folders = settings.WatchFolders().ToList();
            watcher = new ScreenshotWatcher(folders);
            watcher.NewImage += path => line.AddFile(path, temporary: IsOurs(path));
            watcher.Start();
            Log.Note($"watching {string.Join(" | ", folders)}");
        }

        ApplyWatchFolder();

        // 剪贴板通知到达时源程序可能还攥着剪贴板，稍等一下再读。
        var clipboardDelay = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120),
        };
        clipboardDelay.Tick += (_, _) =>
        {
            clipboardDelay.Stop();
            if (!settings.WatchClipboard) return;

            var path = ClipboardImages.TryCapture(AppSettings.ClipboardFolder);
            if (path is not null) line.AddFile(path, temporary: true);
        };
        messages.ClipboardUpdated += () =>
        {
            if (!settings.WatchClipboard) return;

            // 我们自己刚点"复制"放进剪贴板的那张，别当成新图再挂一遍。
            if (ClipboardPayload.IsSelfEcho) return;

            clipboardDelay.Stop();
            clipboardDelay.Start();
        };

        // 快捷键可以随时从托盘换，所以注册放在一个能重复调用的地方。
        void ApplyHotkeys()
        {
            messages.UnregisterHotkeys();

            if (settings.ShotHotkeyEnabled
                && HotkeySpec.TryParse(settings.ShotHotkey, out var shotModifiers, out var shotKey))
            {
                Log.Note(messages.RegisterShotHotkey(shotModifiers, shotKey)
                    ? $"shot hotkey: {settings.ShotHotkey}"
                    : $"shot hotkey FAILED: {settings.ShotHotkey}");
            }

            if (settings.HotkeyEnabled
                && HotkeySpec.TryParse(settings.Hotkey, out var lineModifiers, out var lineKey))
            {
                Log.Note(messages.RegisterLineHotkey(lineModifiers, lineKey)
                    ? $"line hotkey: {settings.Hotkey}"
                    : $"line hotkey FAILED: {settings.Hotkey}");
            }
        }

        var capture = new ScreenCapture();

        messages.LineHotkeyPressed += () =>
        {
            Log.Note("line hotkey");
            line.ShowTemporarily();
        };

        messages.ShotHotkeyPressed += () =>
        {
            if (capture.IsActive) return;

            // 先把绳子收起来，等这一帧画完再抓屏。
            line.HideForCapture();
            var delay = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(150),
            };
            delay.Tick += (_, _) =>
            {
                delay.Stop();
                capture.Start(path =>
                {
                    // 截完挂上绳子，同时放进剪贴板，可以立刻 Ctrl+V。
                    line.AddFile(path, temporary: true);

                    var image = ImageLoader.LoadFull(path);
                    if (image is not null) ClipboardPayload.Copy(image);

                    line.ShowTemporarily();
                });
            };
            delay.Start();
        };

        ApplyHotkeys();

        TrayIcon? tray = null;

        // 换语言要重建托盘菜单，所以把构造包成一个能重复调用的方法。
        Action onExit = () =>
        {
            line.Shutdown();
            watcher?.Dispose();
            clipboardDelay.Stop();
            messages.Dispose();
            tray?.Dispose();

            // 退出时把系统截图位置还回去，别留个我们自己目录的指向。
            if (settings.TakeOverScreenshotFolder) ScreenshotFolder.Restore();

            app.Shutdown();
        };

        void RebuildTray()
        {
            tray?.Dispose();
            tray = new TrayIcon(settings, line, ApplyHotkeys, ApplyWatchFolder, ApplyLanguage, onExit);
        }

        void ApplyLanguage()
        {
            L.Use(settings.Language);
            line.ApplyLanguage();
            RebuildTray();
            Log.Note($"language {L.Current}");
        }

        RebuildTray();

        app.Run();
    }
}
