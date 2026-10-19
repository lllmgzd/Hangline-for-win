using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
        Begin(enableSnap: true, (desktop, selection) =>
        {
            var path = selection is null ? null : Save(desktop, selection.Value);
            if (path is not null) onCaptured(path);
        });
    }

    /// 只要选区，不要存盘。长截图拿它当起点：必须纯拖拽框选滚动区域，禁止窗口吸附（防止带入固定标题栏导致拼接重影）。
    public void StartRegion(Action<Int32Rect> onSelected)
    {
        Begin(enableSnap: false, (_, selection) =>
        {
            if (selection is null) return;
            onSelected(selection.Value);
        });
    }

    /// 抓底图、铺遮罩、等用户松手。选完把底图和选区一起交给调用方处置。
    private void Begin(bool enableSnap, Action<System.Drawing.Bitmap, Int32Rect?> onCompleted)
    {
        if (_overlay is not null) return;

        var vx = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
        var vy = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
        var width = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
        var height = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
        if (width <= 0 || height <= 0)
        {
            Log.Note("capture: cannot read virtual screen metrics");
            return;
        }

        var desktop = CaptureBitmap(vx, vy, width, height);
        if (desktop is null)
        {
            Log.Note("capture: cannot read the desktop");
            return;
        }

        IReadOnlyList<Int32Rect> candidateRegions = enableSnap
            ? FindCandidateRegions(vx, vy, width, height)
            : Array.Empty<Int32Rect>();
        var overlay = new CaptureOverlay(desktop, candidateRegions, enableSnap);
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
    private static System.Drawing.Bitmap? CaptureBitmap(int x, int y, int width, int height)
    {
        try
        {
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

    /// 收集屏幕上可见窗口与各个显示器的矩形边界（转为相对于虚拟屏幕原点的像素坐标）。
    private static List<Int32Rect> FindCandidateRegions(int virtualX, int virtualY, int virtualWidth, int virtualHeight)
    {
        var result = new List<Int32Rect>();
        try
        {
            var currentPid = Environment.ProcessId;
            Native.EnumWindows((hWnd, _) =>
            {
                if (!Native.IsWindowVisible(hWnd) || Native.IsIconic(hWnd)) return true;

                Native.GetWindowThreadProcessId(hWnd, out var pid);
                if (pid == currentPid) return true;

                if (Native.DwmGetWindowAttributeInt(hWnd, Native.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
                {
                    return true;
                }

                Native.RECT rect;
                if (Native.DwmGetWindowAttributeRect(hWnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<Native.RECT>()) != 0)
                {
                    if (!Native.GetWindowRect(hWnd, out rect)) return true;
                }

                var left = rect.Left - virtualX;
                var top = rect.Top - virtualY;
                var width = rect.Right - rect.Left;
                var height = rect.Bottom - rect.Top;

                var clampLeft = Math.Clamp(left, 0, virtualWidth);
                var clampTop = Math.Clamp(top, 0, virtualHeight);
                var clampRight = Math.Clamp(left + width, 0, virtualWidth);
                var clampBottom = Math.Clamp(top + height, 0, virtualHeight);

                var cw = clampRight - clampLeft;
                var ch = clampBottom - clampTop;
                if (cw >= 24 && ch >= 24)
                {
                    result.Add(new Int32Rect(clampLeft, clampTop, cw, ch));
                }

                return true;
            }, IntPtr.Zero);

            // 托底：各个显示器自身的完整边界
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var b = screen.Bounds;
                var left = b.X - virtualX;
                var top = b.Y - virtualY;
                var clampLeft = Math.Clamp(left, 0, virtualWidth);
                var clampTop = Math.Clamp(top, 0, virtualHeight);
                var clampRight = Math.Clamp(left + b.Width, 0, virtualWidth);
                var clampBottom = Math.Clamp(top + b.Height, 0, virtualHeight);
                var cw = clampRight - clampLeft;
                var ch = clampBottom - clampTop;
                if (cw >= 24 && ch >= 24)
                {
                    result.Add(new Int32Rect(clampLeft, clampTop, cw, ch));
                }
            }
        }
        catch (Exception exception)
        {
            Log.Note("enumerate windows failed", exception);
        }

        return result;
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

/// 铺满整个桌面的遮罩，负责画压暗、自适应吸附与自由选区。
internal sealed class CaptureOverlay : Window
{
    private readonly System.Drawing.Bitmap _desktop;
    private readonly IReadOnlyList<Int32Rect> _candidateRegions;
    private readonly bool _enableSnap;
    private readonly double _scaleX;
    private readonly double _scaleY;
    private readonly Rectangle[] _shades = new Rectangle[4];
    private readonly Rectangle _border;
    private readonly TextBlock _label;
    private readonly Border _toolbar;

    private Point _origin;
    private bool _isMouseDown;
    private bool _dragging;
    private bool _completed;
    private Int32Rect? _hoveredRegion;
    private Int32Rect? _selectedPixels;
    private Rect? _selectedDip;

    public CaptureOverlay(System.Drawing.Bitmap desktop, IReadOnlyList<Int32Rect> candidateRegions, bool enableSnap)
    {
        _desktop = desktop;
        _candidateRegions = candidateRegions;
        _enableSnap = enableSnap;

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
            _shades[i] = new Rectangle { Fill = shadeBrush, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            canvas.Children.Add(_shades[i]);
        }

        _border = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            StrokeThickness = 1.5,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        canvas.Children.Add(_border);

        _label = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x14, 0x1C)),
            Padding = new Thickness(6, 2, 6, 2),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        canvas.Children.Add(_label);

        var tbPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var cancelBtn = CreateFlatButton(
            $"✕  {L.T("capture.cancel")}",
            Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF),
            Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF),
            Colors.White,
            () => Complete(null));
        var doneBtn = CreateFlatButton(
            $"✓  {L.T("capture.done")}",
            Color.FromRgb(0x25, 0x63, 0xEB),
            Color.FromRgb(0x1D, 0x4E, 0xD8),
            Colors.White,
            () =>
            {
                if (_selectedPixels.HasValue) Complete(_selectedPixels.Value);
            });
        tbPanel.Children.Add(cancelBtn);
        tbPanel.Children.Add(doneBtn);

        _toolbar = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x14, 0x18, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 4, 6, 4),
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black },
            Visibility = Visibility.Collapsed,
            Child = tbPanel,
        };
        _toolbar.MouseDown += (_, e) => e.Handled = true;
        canvas.Children.Add(_toolbar);

        root.Children.Add(canvas);

        root.Children.Add(new TextBlock
        {
            Text = _enableSnap ? L.T("capture.hint") : L.T("capture.dragHint"),
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            FontSize = 13,
            Background = new SolidColorBrush(Color.FromArgb(0xA0, 0x10, 0x14, 0x1C)),
            Padding = new Thickness(10, 5, 10, 5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 26, 0, 0),
            IsHitTestVisible = false,
        });

        Content = root;

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        MouseRightButtonUp += (_, _) =>
        {
            if (_selectedPixels.HasValue)
            {
                _selectedPixels = null;
                _selectedDip = null;
                _toolbar.Visibility = Visibility.Collapsed;
                HideSelection();
                if (!_enableSnap)
                {
                    Complete(null);
                }
                else
                {
                    UpdateHover(Mouse.GetPosition(this));
                }
            }
            else
            {
                Complete(null);
            }
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Complete(null);
            }
            else if (e.Key == Key.Enter)
            {
                if (_selectedPixels.HasValue)
                {
                    Complete(_selectedPixels.Value);
                }
            }
        };

        Loaded += (_, _) =>
        {
            if (_enableSnap)
            {
                var pos = Mouse.GetPosition(this);
                UpdateHover(pos);
            }
        };
    }

    /// 选区（物理像素）。取消时给 null。
    public event Action<Int32Rect?>? Completed;

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (_toolbar.Visibility == Visibility.Visible && _toolbar.IsMouseOver)
        {
            return;
        }

        var pos = e.GetPosition(this);

        if (e.ClickCount == 2 && _selectedPixels.HasValue && _selectedDip.HasValue && _selectedDip.Value.Contains(pos))
        {
            Complete(_selectedPixels.Value);
            return;
        }

        _selectedPixels = null;
        _selectedDip = null;
        _toolbar.Visibility = Visibility.Collapsed;
        _hoveredRegion = null;
        HideSelection();

        _origin = pos;
        _isMouseDown = true;
        _dragging = !_enableSnap;
        CaptureMouse();

        if (!_enableSnap)
        {
            var selection = Selection(_origin, _origin);
            var pixels = ToPixels(selection);
            RenderSelection(selection, pixels.Width, pixels.Height);
        }
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(this);

        if (_isMouseDown)
        {
            if (!_dragging && _enableSnap)
            {
                var diff = current - _origin;
                if (Math.Abs(diff.X) > 4 || Math.Abs(diff.Y) > 4)
                {
                    _dragging = true;
                    _hoveredRegion = null;
                }
            }

            if (_dragging)
            {
                var selection = Selection(_origin, current);
                var pixels = ToPixels(selection);
                RenderSelection(selection, pixels.Width, pixels.Height);
            }
        }
        else if (_enableSnap && _selectedPixels is null)
        {
            UpdateHover(current);
        }
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (!_isMouseDown) return;

        _isMouseDown = false;
        var current = e.GetPosition(this);
        var wasDragging = _dragging;
        _dragging = false;

        if (wasDragging)
        {
            var dip = Selection(_origin, current);
            var pixels = ToPixels(dip);
            if (pixels.Width >= 4 && pixels.Height >= 4)
            {
                _selectedPixels = pixels;
                _selectedDip = dip;
                RenderSelection(dip, pixels.Width, pixels.Height);
                PlaceToolbar(dip);
            }
            else
            {
                _selectedPixels = null;
                _selectedDip = null;
                _toolbar.Visibility = Visibility.Collapsed;
                HideSelection();
                if (_enableSnap) UpdateHover(current);
            }
        }
        else if (_enableSnap)
        {
            var px = (int)Math.Round(current.X * _scaleX);
            var py = (int)Math.Round(current.Y * _scaleY);
            Int32Rect? matched = null;
            for (var i = 0; i < _candidateRegions.Count; i++)
            {
                var r = _candidateRegions[i];
                if (px >= r.X && px < r.X + r.Width && py >= r.Y && py < r.Y + r.Height)
                {
                    matched = r;
                    break;
                }
            }

            if (matched is not null)
            {
                var r = matched.Value;
                var dipRect = new Rect(r.X / _scaleX, r.Y / _scaleY, r.Width / _scaleX, r.Height / _scaleY);
                _selectedPixels = r;
                _selectedDip = dipRect;
                RenderSelection(dipRect, r.Width, r.Height);
                PlaceToolbar(dipRect);
            }
            else
            {
                HideSelection();
            }
        }

        // 状态与选区重绘完成后再释放鼠标捕获，避免 ReleaseMouseCapture 触发的同步 MouseMove 误判重绘
        ReleaseMouseCapture();
    }

    private void UpdateHover(Point dipPos)
    {
        var px = (int)Math.Round(dipPos.X * _scaleX);
        var py = (int)Math.Round(dipPos.Y * _scaleY);

        Int32Rect? matched = null;
        for (var i = 0; i < _candidateRegions.Count; i++)
        {
            var r = _candidateRegions[i];
            if (px >= r.X && px < r.X + r.Width && py >= r.Y && py < r.Y + r.Height)
            {
                matched = r;
                break;
            }
        }

        _hoveredRegion = matched;

        if (matched is not null)
        {
            var r = matched.Value;
            var dipRect = new Rect(r.X / _scaleX, r.Y / _scaleY, r.Width / _scaleX, r.Height / _scaleY);
            RenderSelection(dipRect, r.Width, r.Height);
        }
        else
        {
            HideSelection();
        }
    }

    private void RenderSelection(Rect selection, int pixelWidth, int pixelHeight)
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;

        Place(_shades[0], 0, 0, width, selection.Y);
        Place(_shades[1], 0, selection.Y + selection.Height, width,
            Math.Max(0, height - selection.Y - selection.Height));
        Place(_shades[2], 0, selection.Y, selection.X, selection.Height);
        Place(_shades[3], selection.X + selection.Width, selection.Y,
            Math.Max(0, width - selection.X - selection.Width), selection.Height);

        foreach (var shade in _shades) shade.Visibility = Visibility.Visible;

        Place(_border, selection.X, selection.Y, selection.Width, selection.Height);
        _border.Visibility = Visibility.Visible;

        _label.Text = $"{pixelWidth} × {pixelHeight}";
        Place(_label, selection.X, Math.Max(0, selection.Y - 22), double.NaN, double.NaN);
        _label.Visibility = Visibility.Visible;
    }

    private void HideSelection()
    {
        foreach (var shade in _shades) shade.Visibility = Visibility.Collapsed;
        _border.Visibility = Visibility.Collapsed;
        _label.Visibility = Visibility.Collapsed;
    }

    private void PlaceToolbar(Rect selectionDip)
    {
        _toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tbWidth = _toolbar.DesiredSize.Width > 0 ? _toolbar.DesiredSize.Width : 140;
        var tbHeight = _toolbar.DesiredSize.Height > 0 ? _toolbar.DesiredSize.Height : 36;

        var screenWidth = ActualWidth > 0 ? ActualWidth : Width;
        var screenHeight = ActualHeight > 0 ? ActualHeight : Height;

        var left = selectionDip.Right - tbWidth;
        var top = selectionDip.Bottom + 6;

        if (top + tbHeight > screenHeight - 6)
        {
            if (selectionDip.Height >= tbHeight + 14)
            {
                top = selectionDip.Bottom - tbHeight - 6;
            }
            else
            {
                top = Math.Max(6, selectionDip.Top - tbHeight - 6);
            }
        }

        left = Math.Clamp(left, 6, Math.Max(6, screenWidth - tbWidth - 6));

        Place(_toolbar, left, top, double.NaN, double.NaN);
        _toolbar.Visibility = Visibility.Visible;
    }

    internal static Button CreateFlatButton(string text, Color normalColor, Color hoverColor, Color textColor, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Foreground = new SolidColorBrush(textColor),
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Cursor = Cursors.Hand,
            Margin = new Thickness(3, 0, 3, 0),
            Padding = new Thickness(12, 5, 12, 5),
            Focusable = false,
        };

        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "border";
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        borderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(normalColor));
        borderFactory.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

        var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(contentFactory);
        template.VisualTree = borderFactory;

        var triggerHover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        triggerHover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(hoverColor), "border"));
        template.Triggers.Add(triggerHover);

        var triggerPressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        triggerPressed.Setters.Add(new Setter(Border.OpacityProperty, 0.85, "border"));
        template.Triggers.Add(triggerPressed);

        button.Template = template;
        button.Click += (_, _) => onClick();
        return button;
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

        if (_isMouseDown)
        {
            _isMouseDown = false;
            ReleaseMouseCapture();
        }

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
