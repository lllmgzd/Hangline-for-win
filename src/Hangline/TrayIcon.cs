using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Hangline;

/// 托盘图标和它的菜单。
internal sealed class TrayIcon : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Hangline";

    /// 能选的快捷键。想加就加一行。
    private static readonly string[] LineHotkeyChoices = { "Ctrl+Alt+T", "Ctrl+Alt+S", "Ctrl+Alt+Q" };

    private static readonly string[] ShotHotkeyChoices = { "Ctrl+Alt+A", "Ctrl+Shift+A", "Ctrl+Alt+F" };

    private readonly AppSettings _settings;
    private readonly LineWindow _line;
    private readonly Action _applyHotkeys;
    private readonly Action _applyWatchFolder;
    private readonly Action _applyLanguage;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _pinItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _foldersMenu;
    private readonly List<ToolStripMenuItem> _lineHotkeyItems = new();
    private readonly List<ToolStripMenuItem> _shotHotkeyItems = new();
    private readonly List<(ToolStripMenuItem Item, string Code)> _languageItems = new();
    private readonly ToolStripMenuItem _lineHotkeyOffItem;
    private readonly ToolStripMenuItem _shotHotkeyOffItem;
    private bool _disposed;

    public TrayIcon(
        AppSettings settings,
        LineWindow line,
        Action applyHotkeys,
        Action applyWatchFolder,
        Action applyLanguage,
        Action onExit)
    {
        _settings = settings;
        _line = line;
        _applyHotkeys = applyHotkeys;
        _applyWatchFolder = applyWatchFolder;
        _applyLanguage = applyLanguage;

        _pinItem = new ToolStripMenuItem(L.T("menu.pin"), null, (_, _) => _line.TogglePinned())
        {
            CheckOnClick = false,
            Checked = settings.Pinned,
        };

        _startupItem = new ToolStripMenuItem(L.T("menu.startup"), null, (_, _) => ToggleStartup())
        {
            Checked = IsStartupEnabled(),
        };

        var clipboardItem = new ToolStripMenuItem(L.T("menu.clipboard"), null, (_, _) =>
        {
            settings.WatchClipboard = !settings.WatchClipboard;
            settings.Save();
        })
        {
            Checked = settings.WatchClipboard,
        };

        var edgeItem = new ToolStripMenuItem(L.T("menu.edge"), null, (_, _) =>
        {
            settings.RevealOnEdge = !settings.RevealOnEdge;
            settings.Save();
        })
        {
            Checked = settings.RevealOnEdge,
        };

        var fullScreenItem = new ToolStripMenuItem(L.T("menu.fullscreen"), null, (_, _) =>
        {
            settings.HideOnFullScreen = !settings.HideOnFullScreen;
            settings.Save();
        })
        {
            Checked = settings.HideOnFullScreen,
        };

        var takeOverItem = new ToolStripMenuItem(L.T("menu.takeover"), null, (_, _) => ToggleTakeOver())
        {
            Checked = settings.TakeOverScreenshotFolder,
        };

        _foldersMenu = new ToolStripMenuItem(L.T("menu.folders"));
        _foldersMenu.DropDownOpening += (_, _) => RebuildFolders();

        var shotMenu = new ToolStripMenuItem(L.T("menu.shotHotkey"));
        foreach (var choice in ShotHotkeyChoices)
        {
            var captured = choice;
            var item = new ToolStripMenuItem(captured, null, (_, _) =>
            {
                settings.ShotHotkey = captured;
                settings.ShotHotkeyEnabled = true;
                settings.Save();
                _applyHotkeys();
            });
            _shotHotkeyItems.Add(item);
            shotMenu.DropDownItems.Add(item);
        }
        shotMenu.DropDownItems.Add(new ToolStripSeparator());
        _shotHotkeyOffItem = new ToolStripMenuItem(L.T("menu.off"), null, (_, _) =>
        {
            settings.ShotHotkeyEnabled = false;
            settings.Save();
            _applyHotkeys();
        });
        shotMenu.DropDownItems.Add(_shotHotkeyOffItem);

        var lineMenu = new ToolStripMenuItem(L.T("menu.lineHotkey"));
        foreach (var choice in LineHotkeyChoices)
        {
            var captured = choice;
            var item = new ToolStripMenuItem(captured, null, (_, _) =>
            {
                settings.Hotkey = captured;
                settings.HotkeyEnabled = true;
                settings.Save();
                _applyHotkeys();
            });
            _lineHotkeyItems.Add(item);
            lineMenu.DropDownItems.Add(item);
        }
        lineMenu.DropDownItems.Add(new ToolStripSeparator());
        _lineHotkeyOffItem = new ToolStripMenuItem(L.T("menu.off"), null, (_, _) =>
        {
            settings.HotkeyEnabled = false;
            settings.Save();
            _applyHotkeys();
        });
        lineMenu.DropDownItems.Add(_lineHotkeyOffItem);

        var languageMenu = new ToolStripMenuItem(L.T("menu.language"));
        foreach (var (code, label) in L.Choices)
        {
            var captured = code;
            var item = new ToolStripMenuItem(label, null, (_, _) => SetLanguage(captured));
            _languageItems.Add((item, captured));
            languageMenu.DropDownItems.Add(item);
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(L.T("menu.toggle"), null, (_, _) => _line.ToggleVisible()));
        menu.Items.Add(_pinItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(shotMenu);
        menu.Items.Add(new ToolStripMenuItem(L.T("menu.rescan"), null, (_, _) => _line.Rescan()));
        menu.Items.Add(new ToolStripMenuItem(L.T("menu.openShots"), null, (_, _) => OpenFolder(settings.ResolvedWatchFolder)));
        menu.Items.Add(new ToolStripMenuItem(L.T("menu.openData"), null, (_, _) => OpenFolder(AppSettings.DataFolder)));
        menu.Items.Add(new ToolStripMenuItem(L.T("menu.clear"), null, (_, _) => _line.ClearLine()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(lineMenu);
        menu.Items.Add(languageMenu);
        menu.Items.Add(clipboardItem);
        menu.Items.Add(edgeItem);
        menu.Items.Add(fullScreenItem);
        menu.Items.Add(takeOverItem);
        menu.Items.Add(_foldersMenu);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(L.T("menu.exit"), null, (_, _) => onExit()));

        menu.Opening += (_, _) =>
        {
            _pinItem.Checked = _line.Pinned;
            _startupItem.Checked = IsStartupEnabled();
            clipboardItem.Checked = settings.WatchClipboard;
            edgeItem.Checked = settings.RevealOnEdge;
            fullScreenItem.Checked = settings.HideOnFullScreen;
            takeOverItem.Checked = settings.TakeOverScreenshotFolder;

            foreach (var item in _shotHotkeyItems)
            {
                item.Checked = settings.ShotHotkeyEnabled
                    && string.Equals(item.Text, settings.ShotHotkey, StringComparison.OrdinalIgnoreCase);
            }
            _shotHotkeyOffItem.Checked = !settings.ShotHotkeyEnabled;

            foreach (var item in _lineHotkeyItems)
            {
                item.Checked = settings.HotkeyEnabled
                    && string.Equals(item.Text, settings.Hotkey, StringComparison.OrdinalIgnoreCase);
            }
            _lineHotkeyOffItem.Checked = !settings.HotkeyEnabled;

            foreach (var (item, code) in _languageItems)
            {
                item.Checked = string.Equals(code, L.Current, StringComparison.OrdinalIgnoreCase);
            }
        };

        _icon = new NotifyIcon
        {
            Text = L.T("tray.name"),
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = menu,
        };

        _icon.DoubleClick += (_, _) => _line.ShowTemporarily();
        _line.ItemsChanged += UpdateText;
        UpdateText();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _line.ItemsChanged -= UpdateText;
        _icon.Visible = false;
        _icon.Dispose();
    }

    private void UpdateText()
    {
        try
        {
            _icon.Text = L.TrayTip(_line.ItemCount);
        }
        catch
        {
            // 提示文字有长度上限，写不进去就用默认的。
        }
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var path = IconFactory.EnsureIconFile();
            return new System.Drawing.Icon(path, new System.Drawing.Size(32, 32));
        }
        catch
        {
            return System.Drawing.SystemIcons.Application;
        }
    }

    /// 额外监听目录：菜单展开时按当前设置重建一遍。
    private void RebuildFolders()
    {
        _foldersMenu.DropDownItems.Clear();
        _foldersMenu.DropDownItems.Add(
            new ToolStripMenuItem(L.T("menu.addFolder"), null, (_, _) => AddFolder()));

        if (_settings.ExtraFolders.Count == 0) return;
        _foldersMenu.DropDownItems.Add(new ToolStripSeparator());

        foreach (var folder in _settings.ExtraFolders.ToList())
        {
            var captured = folder;
            var sub = new ToolStripMenuItem(Shorten(captured)) { ToolTipText = captured };
            sub.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.openFolder"), null, (_, _) => OpenFolder(captured)));
            sub.DropDownItems.Add(new ToolStripMenuItem(L.T("menu.removeFolder"), null, (_, _) => RemoveFolder(captured)));
            _foldersMenu.DropDownItems.Add(sub);
        }
    }

    private void AddFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = L.T("folder.pick"),
            ShowNewFolderButton = false,
        };

        if (dialog.ShowDialog() != DialogResult.OK) return;
        var chosen = dialog.SelectedPath;
        if (string.IsNullOrWhiteSpace(chosen)) return;

        if (_settings.ExtraFolders.Any(f => string.Equals(f, chosen, StringComparison.OrdinalIgnoreCase))) return;

        _settings.ExtraFolders.Add(chosen);
        _settings.Save();
        _applyWatchFolder();
        Log.Note($"extra folder added: {chosen}");
    }

    private void RemoveFolder(string folder)
    {
        var removed = _settings.ExtraFolders.RemoveAll(f =>
            string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return;

        _settings.Save();
        _applyWatchFolder();
        Log.Note($"extra folder removed: {folder}");
    }

    private void SetLanguage(string code)
    {
        _settings.Language = code;
        _settings.Save();
        _applyLanguage();
    }

    /// 菜单里只显示最后两级，完整的路径放在提示里。
    private static string Shorten(string folder)
    {
        try
        {
            var trimmed = folder.TrimEnd('\\');
            var parent = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(trimmed));
            var leaf = System.IO.Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(parent) ? leaf : $"{parent}\\{leaf}";
        }
        catch
        {
            return folder;
        }
    }

    private void ToggleTakeOver()
    {
        var enable = !_settings.TakeOverScreenshotFolder;

        if (enable && !ScreenshotFolder.TakeOver(AppSettings.InboxFolder)) return;
        if (!enable) ScreenshotFolder.Restore();

        _settings.TakeOverScreenshotFolder = enable;
        _settings.Save();
        _applyWatchFolder();
    }

    private void ToggleStartup()
    {
        var enable = !IsStartupEnabled();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;

            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                key.SetValue(RunValue, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(RunValue, throwOnMissingValue: false);
            }
        }
        catch
        {
            // 注册表写不进去就算了，不影响本次运行。
        }
        finally
        {
            _startupItem.Checked = IsStartupEnabled();
        }
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(RunValue) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void OpenFolder(string folder)
    {
        try
        {
            System.IO.Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch
        {
            // 打不开就算了。
        }
    }
}
