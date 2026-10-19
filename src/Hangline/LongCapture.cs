using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Drawing = System.Drawing;
using DrawingImaging = System.Drawing.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace Hangline;

/// 长截图。
///
/// 程序自己不滚动页面——让它去灌滚轮消息，浏览器、Electron、自绘控件的反应差别太大。
/// 这里每隔半秒抓一帧用户选定区域，自己找出相邻两帧之间的纵向位移再拼起来：
/// 新帧顶部与上一帧重合的部分已经画过，底下那截才是新露出来的内容。
///
/// 位移计算在 FrameMatcher 里，用 ORB 特征点投票，理由见那个类。
internal sealed class LongCapture
{
    /// 位移小于它算"画面没动"，不算拼进过内容。
    private const int MinScrollRows = 2;

    /// 连续这么多次对不上就明确报错，不再硬拼。
    private const int MaxFailures = 11;

    /// 画面停住这么久（帧）就自动结束。
    private const int SettledFrames = 4;

    /// 高度上限（行）。再长下去内存和贴图都吃不消。
    private const int MaxCanvasHeight = 30000;

    /// 画布面积上限（像素）。
    private const long MaxCanvasPixels = 150_000_000;

    /// 本机内存兜底，防止选了个巨大的区域又滚很久。
    private const long PixelBudget = 64_000_000;

    private const int IntervalMs = 500;

    private readonly MessageWindow _messages;

    private Int32Rect _region;
    private int _width;
    private int _frameHeight;
    private int _maxHeight;

    private byte[] _canvas = Array.Empty<byte>();
    private int _canvasHeight;
    private FrameMatcher? _matcher;
    private int _settled;
    private int _appended;
    private int _failures;

    private DispatcherTimer? _timer;
    private LongHint? _hint;
    private Action<string>? _onCaptured;

    public LongCapture(MessageWindow messages) => _messages = messages;

    public bool IsActive { get; private set; }

    /// region 用屏幕绝对坐标（物理像素）。
    public void Run(Int32Rect region, Action<string> onCaptured)
    {
        if (IsActive) return;
        if (region.Width < 8 || region.Height < 8)
        {
            Log.Note("long capture: region too small");
            return;
        }

        _region = region;
        _width = region.Width;
        _frameHeight = region.Height;

        var byPixels = (int)Math.Min(MaxCanvasPixels / (_width * 4L), PixelBudget / (_width * 4L));
        _maxHeight = Math.Max(1, Math.Min(MaxCanvasHeight, byPixels));

        // 画布要整块放进一个 byte[]，索引是 int，超了就只能砍高度。
        if ((long)_width * 4 * _maxHeight > int.MaxValue)
        {
            _maxHeight = (int)(int.MaxValue / ((long)_width * 4));
        }

        _canvas = new byte[_width * 4 * _maxHeight];
        _canvasHeight = 0;
        _matcher?.Clear();
        _matcher = new FrameMatcher(_width, _frameHeight);
        _settled = 0;
        _appended = 0;
        _failures = 0;
        _onCaptured = onCaptured;
        IsActive = true;

        _messages.RegisterStopHotkey();
        _hint = new LongHint(_region, Stop, Cancel);
        _hint.Show();

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(IntervalMs),
        };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        Log.Note($"long capture start {_width}x{_frameHeight} at {region.X},{region.Y} maxHeight={_maxHeight}");
    }

    public void Stop()
    {
        if (!IsActive) return;
        IsActive = false;

        _timer?.Stop();
        _timer = null;
        _messages.UnregisterStopHotkey();
        _hint?.Close();
        _hint = null;

        var path = Save();
        var callback = _onCaptured;
        _onCaptured = null;
        _canvas = Array.Empty<byte>();
        _matcher?.Clear();
        _matcher = null;

        if (path is not null) callback?.Invoke(path);
    }

    public void Cancel()
    {
        if (!IsActive) return;
        IsActive = false;

        _timer?.Stop();
        _timer = null;
        _messages.UnregisterStopHotkey();
        _hint?.Close();
        _hint = null;

        _onCaptured = null;
        _canvas = Array.Empty<byte>();
        _matcher?.Clear();
        _matcher = null;
        Log.Note("long capture cancelled");
    }

    private void Tick()
    {
        var matcher = _matcher;
        if (matcher is null) return;

        var frame = GrabFrame();
        if (frame is null) return;

        if (_canvasHeight == 0)
        {
            Append(frame, 0, _frameHeight);
            matcher.Remember(frame);
            return;
        }

        var scroll = matcher.Scroll(frame);
        if (scroll is null)
        {
            // 没拼上：可能抓到了滚动中的一帧，也可能内容变了太多。基准帧保持不变继续比，
            // 但要计失败次数——连续多次对不上就明确报错，别硬拼一张烂图出来。
            _failures++;
            if (_failures < MaxFailures) return;

            Log.Note($"long capture: {_failures} frames could not be matched, giving up");
            _hint?.ShowMessage(L.T("long.failed"));
            return;
        }

        // 页面往下滚时，特征点在新帧里是往上走的，所以位移是负数；取绝对值才是滚动行数。
        var rows = (int)Math.Round(Math.Abs(scroll.Value), MidpointRounding.AwayFromZero);

        if (rows < MinScrollRows)
        {
            // 画面没动。刚选完区域时用户还没开始滚，这时候不能收工，
            // 要等真拼进去过内容之后再算"停住了"。
            if (_appended == 0) return;

            _settled++;
            if (_settled >= SettledFrames) Stop();
            return;
        }

        if (rows > _frameHeight)
        {
            Log.Note($"long capture: scrolled {rows}px in one step, more than the region height");
            return;
        }

        _failures = 0;
        _settled = 0;
        _appended++;
        Append(frame, _frameHeight - rows, _frameHeight);

        if (_canvasHeight >= _maxHeight - _frameHeight)
        {
            Log.Note("long capture: reached height limit, stopping");
            Stop();
        }
    }

    private byte[]? GrabFrame()
    {
        try
        {
            using var bitmap = new Drawing.Bitmap(
                _width, _frameHeight, DrawingImaging.PixelFormat.Format32bppArgb);
            using (var graphics = Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(_region.X, _region.Y, 0, 0, new Drawing.Size(_width, _frameHeight));
            }

            var locked = bitmap.LockBits(
                new Drawing.Rectangle(0, 0, _width, _frameHeight),
                DrawingImaging.ImageLockMode.ReadOnly,
                DrawingImaging.PixelFormat.Format32bppArgb);
            var buffer = new byte[_width * 4 * _frameHeight];
            Marshal.Copy(locked.Scan0, buffer, 0, buffer.Length);
            bitmap.UnlockBits(locked);
            return buffer;
        }
        catch (Exception exception)
        {
            Log.Note("long capture: cannot grab frame", exception);
            return null;
        }
    }

    private void Append(byte[] frame, int fromRow, int toRow)
    {
        if (fromRow < 0) fromRow = 0;
        var rows = toRow - fromRow;
        if (rows <= 0) return;
        if (_canvasHeight + rows > _maxHeight) rows = _maxHeight - _canvasHeight;
        if (rows <= 0) return;

        var bytes = rows * _width * 4;
        Buffer.BlockCopy(frame, fromRow * _width * 4, _canvas, _canvasHeight * _width * 4, bytes);
        _canvasHeight += rows;
    }

    private string? Save()
    {
        if (_canvasHeight < 2) return null;

        try
        {
            var folder = Path.Combine(AppSettings.DataFolder, "Shots");
            System.IO.Directory.CreateDirectory(folder);

            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");
            var path = Path.Combine(folder, $"Long {stamp}.png");
            var index = 2;
            while (File.Exists(path))
            {
                path = Path.Combine(folder, $"Long {stamp} {index}.png");
                index++;
            }

            using (var result = new Drawing.Bitmap(
                _width, _canvasHeight, DrawingImaging.PixelFormat.Format32bppArgb))
            {
                var locked = result.LockBits(
                    new Drawing.Rectangle(0, 0, _width, _canvasHeight),
                    DrawingImaging.ImageLockMode.WriteOnly,
                    DrawingImaging.PixelFormat.Format32bppArgb);
                Marshal.Copy(_canvas, 0, locked.Scan0, _width * 4 * _canvasHeight);
                result.UnlockBits(locked);
                result.Save(path, DrawingImaging.ImageFormat.Png);
            }

            Log.Note($"long capture saved {Path.GetFileName(path)} {_width}x{_canvasHeight}");
            return path;
        }
        catch (Exception exception)
        {
            Log.Note("long capture save failed", exception);
            return null;
        }
    }
}

/// 长截图提示与控制条：贴在选区外侧，带状态指示、文字提示与「取消」「完成」操作。
internal sealed class LongHint : Window
{
    private readonly TextBlock _text;

    public LongHint(Int32Rect region, Action onDone, Action onCancel)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        AllowsTransparency = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;

        var dot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };

        _text = new TextBlock
        {
            Text = L.T("long.running"),
            Foreground = Brushes.White,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
        };

        var cancel = CaptureOverlay.CreateFlatButton(
            $"✕  {L.T("capture.cancel")}",
            Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF),
            Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF),
            Colors.White,
            onCancel);

        var done = CaptureOverlay.CreateFlatButton(
            $"✓  {L.T("long.done")}",
            Color.FromRgb(0x25, 0x63, 0xEB),
            Color.FromRgb(0x1D, 0x4E, 0xD8),
            Colors.White,
            onDone);

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(dot);
        panel.Children.Add(_text);
        panel.Children.Add(cancel);
        panel.Children.Add(done);

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x14, 0x18, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8),
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black },
            Child = panel,
        };

        Content = card;

        // 贴在选区外面：先试下面，下面放不下就放到上面。
        // 位置一律用物理像素（和选区同一套坐标），不能和 WPF 的 DIP 混着算。
        var screen = System.Windows.Forms.Screen.FromRectangle(new Drawing.Rectangle(
            region.X, region.Y, region.Width, region.Height));

        var left = Math.Max(screen.Bounds.Left + 8, Math.Min(region.X, screen.Bounds.Right - 460));
        var below = region.Y + region.Height + 10;
        var top = below + 54 <= screen.Bounds.Bottom
            ? below
            : Math.Max(screen.Bounds.Top + 8, region.Y - 64);

        SourceInitialized += (_, _) =>
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Native.SetWindowPos(
                handle, Native.HWND_TOPMOST, left, top, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        };
    }

    /// 拼接连续失败时把提示文案换掉，让用户知道该往回滚。
    public void ShowMessage(string message)
    {
        if (Dispatcher.CheckAccess()) _text.Text = message;
        else Dispatcher.BeginInvoke(new Action(() => _text.Text = message));
    }
}
