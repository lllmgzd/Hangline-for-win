using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Hangline;

/// 贴着屏幕上沿的那条绳子。不带焦点、不进任务栏，鼠标不碰它就缩在屏幕外。
internal partial class LineWindow : Window
{
    private readonly AppSettings _settings;
    private readonly LineStore _store = new();
    private readonly DispatcherTimer _cursorTimer;

    private IntPtr _handle;
    private bool _shown;
    private bool _disposed;
    private int _stripHeightPhysical = 220;
    private int _hideCountdown;
    private DateTime _holdUntil = DateTime.MinValue;
    private bool _externalHold;
    private System.Drawing.Rectangle _screenBounds = System.Drawing.Rectangle.Empty;

    public LineWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        Hint.Text = L.T("line.hint");

        _store.MaxItems = Math.Max(1, settings.MaxItems);
        _store.Changed += OnStoreChanged;

        _cursorTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(60),
        };
        _cursorTimer.Tick += OnCursorTick;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    public double CardHeight => Math.Max(64, _settings.CardHeight);

    public int ItemCount => _store.Items.Count;

    public bool Pinned
    {
        get => _settings.Pinned;
        set
        {
            _settings.Pinned = value;
            _settings.Save();
            if (value) ShowLine();
        }
    }

    public event Action? ItemsChanged;

    // ── 生命周期 ────────────────────────────────────────────────

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        // 不抢焦点、不进 Alt+Tab。
        var exStyle = Native.GetWindowLongPtr(_handle, Native.GWL_EXSTYLE).ToInt64();
        exStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
        Native.SetWindowLongPtr(_handle, Native.GWL_EXSTYLE, new IntPtr(exStyle));

        Icon = IconFactory.CreateImage(32);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        if (screen is not null) PositionOn(screen);

        Slide.BeginAnimation(TranslateTransform.YProperty, null);
        Slide.Y = -1000;

        // 上次是固定显示，那就一启动就摆出来。
        if (_settings.Pinned && screen is not null) ShowLine(screen);

        _cursorTimer.Start();
    }

    public void Shutdown()
    {
        _disposed = true;
        _cursorTimer.Stop();
        RecycleOwnCaptures();
        _store.Clear();
    }

    /// 自己截的图存在缓存目录里，没人认领就会变成孤儿，一并送回收站。
    /// 截图目录里的真文件不动。
    private void RecycleOwnCaptures()
    {
        foreach (var item in new List<PegItem>(_store.Items))
        {
            if (item.Temporary) Native.DeleteToRecycleBin(item.FilePath, out _);
        }
    }

    // ── 绳子上挂什么 ────────────────────────────────────────────

    public void AddFile(string path) => AddFile(path, false);

    public void AddFile(string path, bool temporary)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        if (!ImageLoader.IsSupportedImage(path)) return;
        if (_store.Contains(path)) return;
        _ = AddFileAsync(path, temporary);
    }

    private async Task AddFileAsync(string path, bool temporary)
    {
        var edge = Math.Max(160, _settings.ThumbnailEdge);

        LineImage? loaded;
        try
        {
            loaded = await Task.Run(() => ImageLoader.LoadForLine(path, edge));
        }
        catch
        {
            return;
        }

        if (loaded is null || _disposed) return;

        // 截图目录和剪贴板是两条路：Win+Shift+S 截一张图，文件和剪贴板各来一次，
        // 内容一样就只挂一张。
        if (_store.ContainsSignature(loaded.Signature))
        {
            Log.Note($"skip duplicate {Path.GetFileName(path)}");
            return;
        }

        var created = ImageLoader.CreationTime(path);
        if (_disposed) return;

        _store.Add(new PegItem(path, loaded.Thumbnail, loaded.Signature, created, temporary));
        Log.Note($"hang {Path.GetFileName(path)} {loaded.PixelWidth}x{loaded.PixelHeight}");
    }

    /// 换语言之后把界面文案刷新一遍。
    public void ApplyLanguage() => Hint.Text = L.T("line.hint");

    /// 只从绳子上取下，文件一律不动。
    public void Remove(PegItem item) => _store.Remove(item);

    /// 在画图里改过之后，把这张的缩略图重新读一遍。
    public void Reload(PegItem item)
    {
        var index = _store.Items.IndexOf(item);
        if (index >= 0) _ = ReloadAsync(item, index);
    }

    private async Task ReloadAsync(PegItem item, int index)
    {
        var edge = Math.Max(160, _settings.ThumbnailEdge);
        LineImage? loaded;
        try
        {
            loaded = await Task.Run(() => ImageLoader.LoadForLine(item.FilePath, edge));
        }
        catch
        {
            return;
        }

        if (loaded is null || _disposed) return;

        var current = _store.Items.IndexOf(item);
        if (current < 0) return;

        _store.Replace(current, new PegItem(
            item.FilePath, loaded.Thumbnail, loaded.Signature, item.Created, item.Temporary));
    }

    /// ✕ 和菜单里的"丢弃"：从绳子上取下，文件送回收站。
    public void Discard(PegItem item)
    {
        _store.Remove(item);

        if (Native.DeleteToRecycleBin(item.FilePath, out var result))
        {
            Log.Note($"recycled {item.FileName}");
        }
        else
        {
            Log.Note($"recycle FAILED {item.FileName} code=0x{result:X}");
        }
    }

    public void ClearLine()
    {
        RecycleOwnCaptures();
        _store.Clear();
    }

    /// 把目录里最近的几张重新挂上绳子。
    public void Rescan()
    {
        string[] files;
        try
        {
            files = System.IO.Directory.GetFiles(_settings.ResolvedWatchFolder);
        }
        catch
        {
            ShowTemporarily();
            return;
        }

        Array.Sort(files, (a, b) => ImageLoader.CreationTime(a).CompareTo(ImageLoader.CreationTime(b)));

        var take = Math.Min(files.Length, Math.Max(1, _settings.MaxItems));
        for (var i = files.Length - take; i < files.Length; i++)
        {
            if (i >= 0) AddFile(files[i]);
        }

        ShowTemporarily();
    }

    // ── 拉下来 / 收回去 ────────────────────────────────────────

    public void ToggleVisible()
    {
        if (_shown)
        {
            _holdUntil = DateTime.MinValue;
            HideLine();
        }
        else
        {
            ShowTemporarily();
        }
    }

    public void TogglePinned() => Pinned = !Pinned;

    /// 右键菜单开着、或者正在拖图的时候按住不让它收回去。
    public void HoldOpen(bool hold)
    {
        _externalHold = hold;
        _hideCountdown = 0;
    }

    /// 截图之前把绳子收起来，免得自己截进图里。立刻生效，不做动画。
    public void HideForCapture()
    {
        _externalHold = false;
        _holdUntil = DateTime.MinValue;
        _shown = false;
        _hideCountdown = 0;
        Slide.BeginAnimation(TranslateTransform.YProperty, null);
        Slide.Y = -2000;
    }

    /// 临时拉下来几秒，方便鼠标移进去。
    public void ShowTemporarily(int seconds = 4)
    {
        _holdUntil = DateTime.UtcNow.AddSeconds(seconds);
        ShowLine();
    }

    private void ShowLine(System.Windows.Forms.Screen? screen = null)
    {
        if (_settings.HideOnFullScreen && FullScreen.IsActive(_handle)) return;
        if (screen is null && !TryScreenUnderCursor(out screen)) return;
        PositionOn(screen!);

        _shown = true;
        _hideCountdown = 0;
        AnimateTo(0);
    }

    private void HideLine()
    {
        _shown = false;
        _hideCountdown = 0;
        var height = ActualHeight > 0 ? ActualHeight : 220;
        AnimateTo(-height - 24);
    }

    private void AnimateTo(double target)
    {
        var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        Slide.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    /// 把窗口铺在目标屏幕上。坐标走 SetWindowPos 的物理像素，绕开 DPI 换算。
    private void PositionOn(System.Windows.Forms.Screen screen)
    {
        if (_handle == IntPtr.Zero) return;

        var bounds = screen.Bounds;
        if (bounds == _screenBounds) return;
        _screenBounds = bounds;

        var scale = DpiScaleFor(bounds);
        _stripHeightPhysical = (int)Math.Ceiling((CardHeight + 46) * scale);

        Native.SetWindowPos(
            _handle,
            Native.HWND_TOPMOST,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            _stripHeightPhysical,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    private static double DpiScaleFor(System.Drawing.Rectangle bounds)
    {
        try
        {
            var point = new Native.POINT
            {
                X = bounds.Left + bounds.Width / 2,
                Y = bounds.Top + bounds.Height / 2,
            };
            var monitor = Native.MonitorFromPoint(point, Native.MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero &&
                Native.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 &&
                dpiX > 0)
            {
                return dpiX / 96.0;
            }
        }
        catch
        {
            // shcore 取不到就按 100% 算。
        }
        return 1.0;
    }

    private static bool TryScreenUnderCursor(out System.Windows.Forms.Screen? screen)
    {
        screen = null;
        if (!Native.GetCursorPos(out var cursor)) return false;
        screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y));
        return screen is not null;
    }

    // ── 鼠标碰边就拉下来 ────────────────────────────────────────

    private void OnCursorTick(object? sender, EventArgs e)
    {
        if (!Native.GetCursorPos(out var cursor)) return;

        // 全屏游戏、全屏视频的时候整条缩起来，别挡着。
        if (_settings.HideOnFullScreen && FullScreen.IsActive(_handle))
        {
            if (_shown) HideLine();
            return;
        }

        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y));
        if (screen is null) return;

        var top = screen.Bounds.Top;
        var bandBottom = top + _stripHeightPhysical + 10;
        var onBand = cursor.Y >= top && cursor.Y <= bandBottom;

        if (_shown)
        {
            if (_settings.Pinned || _externalHold || DateTime.UtcNow < _holdUntil)
            {
                _hideCountdown = 0;
                return;
            }

            if (onBand)
            {
                _hideCountdown = 0;
                return;
            }

            // 约 360ms 的缓冲，免得手一抖就收回去。
            if (++_hideCountdown < 6) return;
            HideLine();
            return;
        }

        if (!_settings.RevealOnEdge) return;
        if (cursor.Y <= top + Math.Max(0, _settings.EdgeTriggerPx)) ShowLine(screen);
    }

    // ── 卡片 ────────────────────────────────────────────────────

    private void OnStoreChanged()
    {
        ItemsPanel.Children.Clear();
        foreach (var item in _store.Items) ItemsPanel.Children.Add(new PegCard(item, this));

        Hint.Visibility = _store.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ItemsChanged?.Invoke();
    }
}
