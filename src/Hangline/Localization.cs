using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Hangline;

/// 界面文案。
///
/// 加一种语言 = 在 Table 里加一段 "key=文案" 的文本，别的地方都不用改。
/// 键名写错不会崩，会原样显示键名，方便一眼看出漏了哪条。
internal static class L
{
    public const string System = "system";

    private static readonly Dictionary<string, string> Simplified = Parse(@"
app.name=晾绳
tray.name=晾绳 Hangline
tray.count=晾绳 Hangline — 挂着 {0} 张
menu.toggle=展开 / 收起
menu.pin=固定显示
menu.shotHotkey=截图快捷键
menu.lineHotkey=展开 / 收起快捷键
menu.off=关闭
menu.rescan=重新挂上最近的截图
menu.openShots=打开截图文件夹
menu.openData=打开数据目录
menu.openFolder=打开
menu.clear=清空绳子
menu.clipboard=剪贴板里的图片也挂上
menu.edge=鼠标碰上沿就拉下来
menu.fullscreen=全屏应用时自动隐藏
menu.takeover=接管系统截图目录
menu.folders=额外监听目录
menu.addFolder=添加目录…
menu.removeFolder=移除
menu.startup=开机启动
menu.language=语言
menu.systemLanguage=跟随系统
menu.exit=退出
folder.pick=选择要额外监听的目录
card.tip=单击复制 · 双击打开 · 长按用画图编辑 · 拖出去发送
card.copy=复制图片
card.open=打开
card.paint=用画图编辑
card.reveal=在文件夹中显示
card.saveAs=另存为…
card.remove=从绳子上取下（不删文件）
card.discard=丢弃（删到回收站）
line.hint=截好的图会挂在这条绳子上
capture.hint=拖动选择区域，Esc 或右键取消
dialog.imageFilter=图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*
");

    private static readonly Dictionary<string, string> Traditional = Parse(@"
app.name=晾繩
tray.name=晾繩 Hangline
tray.count=晾繩 Hangline — 掛著 {0} 張
menu.toggle=展開 / 收合
menu.pin=固定顯示
menu.shotHotkey=截圖快速鍵
menu.lineHotkey=展開 / 收合快速鍵
menu.off=關閉
menu.rescan=重新掛上最近的截圖
menu.openShots=開啟截圖資料夾
menu.openData=開啟資料夾
menu.openFolder=開啟
menu.clear=清空繩子
menu.clipboard=剪貼簿裡的圖片也掛上
menu.edge=滑鼠碰上緣就拉下來
menu.fullscreen=全螢幕應用程式時自動隱藏
menu.takeover=接管系統截圖資料夾
menu.folders=額外監看資料夾
menu.addFolder=新增資料夾…
menu.removeFolder=移除
menu.startup=開機啟動
menu.language=語言
menu.systemLanguage=跟隨系統
menu.exit=結束
folder.pick=選擇要額外監看的資料夾
card.tip=單擊複製 · 雙擊開啟 · 長按用畫圖編輯 · 拖出去傳送
card.copy=複製圖片
card.open=開啟
card.paint=用畫圖編輯
card.reveal=在資料夾中顯示
card.saveAs=另存新檔…
card.remove=從繩子上取下（不刪檔案）
card.discard=丟棄（刪到回收站）
line.hint=截好的圖會掛在這條繩子上
capture.hint=拖曳選取區域，Esc 或按右鍵取消
dialog.imageFilter=圖片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有檔案|*.*
");

    private static readonly Dictionary<string, string> English = Parse(@"
app.name=Hangline
tray.name=Hangline
tray.count=Hangline — {0} on the line
menu.toggle=Show / hide
menu.pin=Keep it down
menu.shotHotkey=Screenshot shortcut
menu.lineHotkey=Show / hide shortcut
menu.off=Off
menu.rescan=Hang up the latest screenshots
menu.openShots=Open screenshots folder
menu.openData=Open data folder
menu.openFolder=Open
menu.clear=Clear the line
menu.clipboard=Hang copied images too
menu.edge=Drop down when the pointer hits the top edge
menu.fullscreen=Hide in full-screen apps
menu.takeover=Take over the system screenshot folder
menu.folders=Extra folders to watch
menu.addFolder=Add a folder…
menu.removeFolder=Remove
menu.startup=Start with Windows
menu.language=Language
menu.systemLanguage=Follow the system
menu.exit=Quit
folder.pick=Pick a folder to watch as well
card.tip=Click to copy · Double-click to open · Hold to edit in Paint · Drag out to send
card.copy=Copy image
card.open=Open
card.paint=Edit in Paint
card.reveal=Show in folder
card.saveAs=Save as…
card.remove=Take off the line (keep the file)
card.discard=Discard (send to Recycle Bin)
line.hint=Screenshots you take will hang here
capture.hint=Drag to pick a region. Esc or right-click to cancel
dialog.imageFilter=Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*
");

    private static readonly Dictionary<string, string> Japanese = Parse(@"
app.name=ハングライン
tray.name=ハングライン Hangline
tray.count=ハングライン — {0} 枚
menu.toggle=表示 / 隠す
menu.pin=表示したまま
menu.shotHotkey=スクリーンショットのキー
menu.lineHotkey=表示 / 隠すキー
menu.off=オフ
menu.rescan=最近のスクリーンショットを掛け直す
menu.openShots=スクリーンショットのフォルダーを開く
menu.openData=データフォルダーを開く
menu.openFolder=開く
menu.clear=ラインを空にする
menu.clipboard=コピーした画像も掛ける
menu.edge=カーソルが上端に触れたら降ろす
menu.fullscreen=全画面アプリでは隠す
menu.takeover=システムの保存先を引き継ぐ
menu.folders=追加で監視するフォルダー
menu.addFolder=フォルダーを追加…
menu.removeFolder=削除
menu.startup=Windows と一緒に起動
menu.language=言語
menu.systemLanguage=システムに合わせる
menu.exit=終了
folder.pick=追加で監視するフォルダーを選ぶ
card.tip=クリックでコピー · ダブルクリックで開く · 長押しでペイント · ドラッグで送信
card.copy=画像をコピー
card.open=開く
card.paint=ペイントで編集
card.reveal=フォルダーで表示
card.saveAs=名前を付けて保存…
card.remove=ラインから外す（ファイルは残す）
card.discard=捨てる（ごみ箱へ）
line.hint=撮ったスクリーンショットがここに掛かります
capture.hint=ドラッグで範囲を選択。Esc か右クリックで取消
dialog.imageFilter=画像|*.png;*.jpg;*.jpeg;*.bmp;*.gif|すべてのファイル|*.*
");

    private static readonly Dictionary<string, Dictionary<string, string>> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh-Hans"] = Simplified,
        ["zh-Hant"] = Traditional,
        ["en"] = English,
        ["ja"] = Japanese,
    };

    private static Dictionary<string, string> _current = Simplified;

    public static IReadOnlyList<(string Code, string Label)> Choices { get; } = new[]
    {
        (System, "跟随系统 / System"),
        ("zh-Hans", "简体中文"),
        ("zh-Hant", "繁體中文"),
        ("en", "English"),
        ("ja", "日本語"),
    };

    public static string Current { get; private set; } = System;

    public static void Use(string? language)
    {
        Current = string.IsNullOrWhiteSpace(language) ? System : language!;
        var code = Current == System ? FromCulture(CultureInfo.CurrentUICulture.Name) : Normalize(Current);
        _current = Table.TryGetValue(code, out var table) ? table : Simplified;
    }

    public static string T(string key) => _current.TryGetValue(key, out var text) ? text : key;

    public static string TrayTip(int count) =>
        count == 0 ? T("tray.name") : string.Format(T("tray.count"), count);

    public static string CardTip(string fileName) => $"{fileName}{Environment.NewLine}{T("card.tip")}";

    private static string Normalize(string requested) => requested.ToLowerInvariant() switch
    {
        "zh-hans" or "zh-cn" or "zh-sg" => "zh-Hans",
        "zh-hant" or "zh-tw" or "zh-hk" or "zh-mo" => "zh-Hant",
        "ja" or "ja-jp" => "ja",
        "en" or "en-us" or "en-gb" => "en",
        _ => requested,
    };

    private static string FromCulture(string name)
    {
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return name.Contains("TW", StringComparison.OrdinalIgnoreCase)
                || name.Contains("HK", StringComparison.OrdinalIgnoreCase)
                || name.Contains("MO", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                ? "zh-Hant"
                : "zh-Hans";
        }

        if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
        return "en";
    }

    /// 每行一条 "键=文案"，空行和没有等号的行跳过。
    private static Dictionary<string, string> Parse(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var index = line.IndexOf('=');
            if (index <= 0) continue;

            map[line[..index].Trim()] = line[(index + 1)..].Trim();
        }
        return map;
    }
}
