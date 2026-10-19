using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using IoPath = System.IO.Path;

namespace Hangline;

/// 框选截图。
///
/// 按下快捷键时先抓一张整个桌面的底图，用它铺满一块遮罩窗口：选中的地方原样露出来，
/// 其余部分压暗。松开鼠标就把那一块裁下来，存成 PNG。
internal sealed class ScreenCapture
{
    private CaptureOverlay? _overlay;

    public bool IsActive => _overlay is not null;

    public void Start(Action<string> onCaptured)
    {
        Begin((desktop, selection) =>
        {
            var path = selection is null ? null : Save(desktop, selection.Value);
            if (path is not null) onCaptured(path);
        });
    }

    /// 只要选区，不要存盘。长截图拿它当起点：先框一块区域，之后自己滚页面。
    public void StartRegion(Action<Int32Rect> onSelected)
    {
        Begin((_, selection) =>
        {
            if (selection is null) return;
            onSelected(selection.Value);
        });
    }

    /// 抓底图、铺遮罩、等用户松手。选完把底图和选区一起交给调用方处置。
    private void Begin(Action<System.Drawing.Bitmap, Int32Rect?> onCompleted)
    {
        if (_overlay is not null) return;

        var desktop = CaptureVirtualScreen();
        if (desktop is null)
        {
            Log.Note("capture: cannot read the desktop");
            return;
        }

        var overlay = new CaptureOverlay(desktop);
        _overlay = overlay;
        var handler = onCompleted;
        overlay.Completed += selection =>
        {
            _overlay = null;
            try
            {
                handler(desktop, selection);
            }
            finally
            {
                desktop.Dispose();
            }
        };

        overlay.Show();
        overlay.Activate();
    }

    /// 整个虚拟桌面的物理像素快照。
    private static System.Drawing.Bitmap? CaptureVirtualScreen()
    {
        try
        {
            var x = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
            var y = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            var width = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
            var height = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
            if (width <= 0 || height <= 0) return null;

            var bitmap = new System.Drawing.Bitmap(
                width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
            }
            return bitmap;
        }
        catch (Exception exception)
        {
            Log.Note("capture failed", exception);
            return null;
        }
    }

    private static string? Save(System.Drawing.Bitmap desktop, Int32Rect selection)
    {
        if (selection.Width < 2 || selection.Height < 2) return null;

        try
        {
            var folder = IoPath.Combine(AppSettings.DataFolder, "Shots");
            System.IO.Directory.CreateDirectory(folder);

            var bounds = new System.Drawing.Rectangle(
                selection.X, selection.Y, selection.Width, selection.Height);
            using var cropped = desktop.Clone(bounds, desktop.PixelFormat);

            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");
            var path = IoPath.Combine(folder, $"Screenshot {stamp}.png");
            var index = 2;
            while (File.Exists(path))
            {
                path = IoPath.Combine(folder, $"Screenshot {stamp} {index}.png");
                index++;
            }

            cropped.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Log.Note($"captured {IoPath.GetFileName(path)} {selection.Width}x{selection.Height}");
            return path;
        }
        catch (Exception exception)
        {
            Log.Note("capture save failed", exception);
            return null;
        }
    }
}

/// 铺满整个桌面的遮罩，负责画压暗和选区。
internal sealed class CaptureOverlay : Window
{
    private readonly System.Drawing.Bitmap _desktop;
    private readonly double _scaleX;
    private readonly double _scaleY;
    private readonly Rectangle[] _shades = new Rectangle[4];
    private readonly Rectangle _border;
    private readonly TextBlock _label;

    private Point _origin;
    private bool _dragging;
    private bool _completed;

    public CaptureOverlay(System.Drawing.Bitmap desktop)
    {
        _desktop = desktop;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Focusable = true;
        Cursor = Cursors.Cross;
        Background = Brushes.Black;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        // 底图是物理像素，遮罩是 DIP，这个比例就是两者之间的换算。
        _scaleX = Width > 0 ? desktop.Width / Width : 1;
        _scaleY = Height > 0 ? desktop.Height / Height : 1;

        var root = new Grid();
        root.Children.Add(new Image
        {
            Source = ToSource(desktop),
            Stretch = Stretch.Fill,
        });

        var canvas = new Canvas();
        var shadeBrush = new SolidColorBrush(Color.FromArgb(0x8C, 0x06, 0x0A, 0x12));
        for (var i = 0; i < _shades.Length; i++)
        {
            _shades[i] = new Rectangle { Fill = shadeBrush, Visibility = Visibility.Collapsed };
            canvas.Children.Add(_shades[i]);
        }

        _border = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            StrokeThickness = 1.5,
            Visibility = Visibility.Collapsed,
        };
        canvas.Children.Add(_border);

        _label = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x14, 0x1C)),
            Padding = new Thickness(6, 2, 6, 2),
            Visibility = Visibility.Collapsed,
        };
        canvas.Children.Add(_label);
        root.Children.Add(canvas);

        root.Children.Add(new TextBlock
        {
            Text = L.T("capture.hint"),
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            FontSize = 13,
            Background = new SolidColorBrush(Color.FromArgb(0xA0, 0x10, 0x14, 0x1C)),
            Padding = new Thickness(10, 5, 10, 5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 26, 0, 0),
        });

        Content = root;

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        MouseRightButtonUp += (_, _) => Complete(null);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Complete(null);
        };
    }

    /// 选区（物理像素）。取消时给 null。
    public event Action<Int32Rect?>? Completed;

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _origin = e.GetPosition(this);
        _dragging = true;
        _border.Visibility = Visibility.Visible;
        _label.Visibility = Visibility.Visible;
        foreach (var shade in _shades) shade.Visibility = Visibility.Visible;
        Update(_origin, _origin);
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        Update(_origin, e.GetPosition(this));
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Complete(ToPixels(Selection(_origin, e.GetPosition(this))));
    }

    private void Update(Point from, Point to)
    {
        var selection = Selection(from, to);
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;

        Place(_shades[0], 0, 0, width, selection.Y);
        Place(_shades[1], 0, selection.Y + selection.Height, width,
            Math.Max(0, height - selection.Y - selection.Height));
        Place(_shades[2], 0, selection.Y, selection.X, selection.Height);
        Place(_shades[3], selection.X + selection.Width, selection.Y,
            Math.Max(0, width - selection.X - selection.Width), selection.Height);

        Place(_border, selection.X, selection.Y, selection.Width, selection.Height);

        var pixels = ToPixels(selection);
        _label.Text = $"{pixels.Width} × {pixels.Height}";
        Place(_label, selection.X, Math.Max(0, selection.Y - 22), double.NaN, double.NaN);
    }

    private static void Place(UIElement element, double x, double y, double width, double height)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        if (!double.IsNaN(width)) element.SetValue(FrameworkElement.WidthProperty, width);
        if (!double.IsNaN(height)) element.SetValue(FrameworkElement.HeightProperty, height);
    }

    private static Rect Selection(Point from, Point to) => new(
        Math.Min(from.X, to.X),
        Math.Min(from.Y, to.Y),
        Math.Abs(from.X - to.X),
        Math.Abs(from.Y - to.Y));

    private Int32Rect ToPixels(Rect dip)
    {
        var x = (int)Math.Round(dip.X * _scaleX);
        var y = (int)Math.Round(dip.Y * _scaleY);
        var width = (int)Math.Round(dip.Width * _scaleX);
        var height = (int)Math.Round(dip.Height * _scaleY);

        x = Math.Clamp(x, 0, Math.Max(0, _desktop.Width - 1));
        y = Math.Clamp(y, 0, Math.Max(0, _desktop.Height - 1));
        width = Math.Clamp(width, 0, _desktop.Width - x);
        height = Math.Clamp(height, 0, _desktop.Height - y);

        return new Int32Rect(x, y, width, height);
    }

    private void Complete(Int32Rect? selection)
    {
        if (_completed) return;
        _completed = true;

        var handler = Completed;
        Completed = null;
        Close();
        handler?.Invoke(selection);
    }

    private static BitmapSource ToSource(System.Drawing.Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }
}
