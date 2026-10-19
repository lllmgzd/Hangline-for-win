using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hangline;

/// 用户设置，存在数据目录下的 settings.json。
internal sealed class AppSettings
{
    /// 盯着的截图目录。空则由 Windows 的图片目录推算。
    public string? WatchFolder { get; set; }

    /// 绳子上最多挂几张，多了从最旧的开始掉。
    public int MaxItems { get; set; } = 12;

    /// 卡片高度（DIP）。
    public int CardHeight { get; set; } = 128;

    /// 缩略图最长边（像素）。
    public int ThumbnailEdge { get; set; } = 640;

    /// 鼠标贴到屏幕上沿多少像素内算"碰绳子"。
    public int EdgeTriggerPx { get; set; } = 3;

    /// 固定显示，不自动收。
    public bool Pinned { get; set; }

    /// 鼠标碰上沿就拉下来。
    public bool RevealOnEdge { get; set; } = true;

    /// 剪贴板里的图片也挂上来。
    public bool WatchClipboard { get; set; } = true;

    /// 全局快捷键开关。
    public bool HotkeyEnabled { get; set; } = true;

    /// 展开 / 收起绳子的快捷键。
    public string Hotkey { get; set; } = "Ctrl+Alt+T";

    /// 框选截图的快捷键开关。
    public bool ShotHotkeyEnabled { get; set; } = true;

    /// 框选截图的快捷键：按下后拖出一块区域，截完挂上绳子。
    public string ShotHotkey { get; set; } = "Ctrl+Alt+A";

    /// 全屏应用（游戏、视频）时自动隐藏，别挡着。
    public bool HideOnFullScreen { get; set; } = true;

    /// 接管系统截图目录：把系统截图直接存到晾绳这边来。
    public bool TakeOverScreenshotFolder { get; set; }

    /// 除了系统截图目录，额外还要盯的目录。
    public List<string> ExtraFolders { get; set; } = new();

    /// 界面语言：system / zh-Hans / zh-Hant / en / ja。
    public string Language { get; set; } = L.System;

    /// 实际要盯的目录：系统截图目录（或接管的目录）加上用户额外加的。
    public IReadOnlyList<string> WatchFolders()
    {
        var folders = new List<string>
        {
            TakeOverScreenshotFolder ? InboxFolder : ResolvedWatchFolder,
        };

        foreach (var extra in ExtraFolders)
        {
            if (string.IsNullOrWhiteSpace(extra)) continue;
            if (folders.Any(f => string.Equals(f, extra, StringComparison.OrdinalIgnoreCase))) continue;
            folders.Add(extra);
        }

        return folders;
    }

    /// 配置、日志、剪贴板缓存、截图都放这里。
    /// 便携做法：跟在程序旁边，不往 C 盘的 AppData 里堆。
    public static string DataFolder { get; } = ResolveDataFolder();

    private static string ResolveDataFolder()
    {
        try
        {
            var beside = Path.Combine(AppContext.BaseDirectory, "data");
            System.IO.Directory.CreateDirectory(beside);
            return beside;
        }
        catch
        {
            // 程序目录写不进去（比如装在 Program Files），退回用户目录。
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hangline");
            try { System.IO.Directory.CreateDirectory(fallback); } catch { /* 兜底也失败就只能这样了 */ }
            return fallback;
        }
    }

    private static string FilePath => Path.Combine(DataFolder, "settings.json");

    /// 剪贴板里接住的图片落在这里。
    public static string ClipboardFolder => Path.Combine(DataFolder, "Clipboard");

    /// 系统截图接管过来之后，图落在这里。
    public static string InboxFolder => Path.Combine(DataFolder, "Inbox");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // 不然快捷键里的 + 会写成 \u002B。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (loaded is not null) return loaded;
            }
        }
        catch (Exception exception)
        {
            // 配置读坏了就退回默认值，不挡启动；但记一笔，免得设置"莫名其妙丢了"。
            Log.Note("settings load failed, using defaults", exception);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(DataFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // 存不下不影响本次运行。
        }
    }

    /// Windows 默认把截图放在"图片"下的 Screenshots 里。
    public static string DefaultWatchFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    /// 算出来的，不往配置文件里写。
    [JsonIgnore]
    public string ResolvedWatchFolder =>
        string.IsNullOrWhiteSpace(WatchFolder) ? DefaultWatchFolder : WatchFolder!;
}
