using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Hangline;

/// 绳子上的一张图。
/// 单击复制，双击打开，长按丢进画图，拖出去交给别的程序，右键出菜单。
internal sealed class PegCard : Border
{
    private static readonly TimeSpan LongPressDelay = TimeSpan.FromMilliseconds(520);

    private readonly PegItem _item;
    private readonly LineWindow _line;
    private readonly Border _close;

    private Point _downPoint;
    private bool _suppressClick;
    private DispatcherTimer? _pressTimer;
    private DispatcherTimer? _flashTimer;

    /// 这个窗口不抢焦点，拿不到系统的双击消息（clickCount 一直是 1），
    /// 所以双击自己按时间和位置判定。
    private DateTime _lastClickAt = DateTime.MinValue;
    private Point _lastClickPoint;

    public PegCard(PegItem item, LineWindow line)
    {
        _item = item;
        _line = line;

        var height = line.CardHeight;
        var ratio = item.Thumbnail.PixelHeight > 0
            ? (double)item.Thumbnail.PixelWidth / item.Thumbnail.PixelHeight
            : 1.0;

        Height = height;
        Width = Math.Max(52, Math.Min(height * 1.9, height * ratio));
        Margin = new Thickness(9, 0, 9, 0);
        CornerRadius = new CornerRadius(9);
        BorderThickness = new Thickness(1.3);
        BorderBrush = GlassBorder();
        Background = new SolidColorBrush(Color.FromArgb(0x22, 0x0C, 0x14, 0x24));
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
        ToolTip = L.CardTip(item.FileName);

        var image = new Image
        {
            Source = item.Thumbnail,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(5, 7, 5, 5),
        };

        _close = BuildClose();

        var content = new Grid();
        content.Children.Add(image);
        content.Children.Add(Clip());
        content.Children.Add(_close);
        Child = content;

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        MouseRightButtonUp += OnMouseRightUp;
        MouseEnter += (_, _) => _close.Visibility = Visibility.Visible;
        MouseLeave += (_, _) => _close.Visibility = Visibility.Collapsed;

        PrepareEntrance();
    }

    /// 玻璃边框：上亮下暗，比一条纯色边有层次。
    private static Brush GlassBorder()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0.6, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x2A, 0x0A, 0x12, 0x20), 1));
        return brush;
    }

    /// 挂在绳子上的那个小夹子。
    private static Border Clip() => new()
    {
        Width = 15,
        Height = 7,
        CornerRadius = new CornerRadius(3),
        Background = new SolidColorBrush(Color.FromArgb(0xE6, 0xE6, 0xEB, 0xF5)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0x1C, 0x2A, 0x44)),
        BorderThickness = new Thickness(1),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, -4, 0, 0),
        IsHitTestVisible = false,
    };

    /// 挂上去：先淡入、从上落下来，再轻轻晃两下稳住。
    private void PrepareEntrance()
    {
        var drop = new TranslateTransform(0, -18);
        var tilt = new RotateTransform(0);
        var group = new TransformGroup();
        group.Children.Add(drop);
        group.Children.Add(tilt);
        RenderTransformOrigin = new Point(0.5, 0);
        RenderTransform = group;
        Opacity = 0;

        Loaded += (_, _) =>
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(170)));
            drop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });

            var sway = new DoubleAnimationUsingKeyFrames();
            sway.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            sway.KeyFrames.Add(new EasingDoubleKeyFrame(2.6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(210))));
            sway.KeyFrames.Add(new EasingDoubleKeyFrame(-1.7, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(430))));
            sway.KeyFrames.Add(new EasingDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(620))));
            sway.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(800))));
            tilt.BeginAnimation(RotateTransform.AngleProperty, sway);
        };
    }

    private Border BuildClose()
    {
        var glyph = new TextBlock
        {
            Text = "✕",
            FontSize = 10,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var close = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x14, 0x18, 0x20)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0),
            Visibility = Visibility.Collapsed,
            Cursor = Cursors.Hand,
            ToolTip = L.T("card.discard"),
            Child = glyph,
        };

        close.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CancelPress();
            _line.Discard(_item);
        };

        return close;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _downPoint = e.GetPosition(this);
        _suppressClick = false;

        CancelPress();
        _pressTimer = new DispatcherTimer { Interval = LongPressDelay };
        _pressTimer.Tick += (_, _) =>
        {
            CancelPress();
            _suppressClick = true;
            EditInPaint();
        };
        _pressTimer.Start();
    }

    private void CancelPress()
    {
        _pressTimer?.Stop();
        _pressTimer = null;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _downPoint.X) < 6 && Math.Abs(current.Y - _downPoint.Y) < 6) return;

        CancelPress();
        _suppressClick = true;
        // 拖的途中鼠标会离开顶部，先按住不让绳子收回去。
        _line.HoldOpen(true);
        try
        {
            // 拖到文件夹就是拷一份过去，拖到聊天窗口就是发出去。
            var data = new DataObject(DataFormats.FileDrop, new[] { _item.FilePath });
            Log.Note($"drag start {_item.FileName}");
            var effect = DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
            Log.Note($"drag end {_item.FileName} effect={effect}");
        }
        catch (Exception exception)
        {
            Log.Note("drag failed", exception);
        }
        finally
        {
            _line.HoldOpen(false);
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        CancelPress();

        if (_suppressClick)
        {
            _suppressClick = false;
            return;
        }

        if (e.ClickCount >= 2 || IsSecondClick(e.GetPosition(this)))
        {
            OpenFile();
            return;
        }

        CopyImage();
    }

    /// 距上次单击不到 450 毫秒、位置也没怎么动，就算双击。
    private bool IsSecondClick(Point at)
    {
        var now = DateTime.UtcNow;
        var isDouble = now - _lastClickAt < TimeSpan.FromMilliseconds(450)
            && Math.Abs(at.X - _lastClickPoint.X) < 8
            && Math.Abs(at.Y - _lastClickPoint.Y) < 8;

        _lastClickAt = now;
        _lastClickPoint = at;
        return isDouble;
    }

    private void OnMouseRightUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CancelPress();

        var menu = new ContextMenu
        {
            PlacementTarget = this,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };

        menu.Items.Add(BuildItem(L.T("card.copy"), CopyImage));
        menu.Items.Add(BuildItem(L.T("card.open"), OpenFile));
        menu.Items.Add(BuildItem(L.T("card.paint"), EditInPaint));
        menu.Items.Add(BuildItem(L.T("card.reveal"), RevealInFolder));
        menu.Items.Add(BuildItem(L.T("card.saveAs"), SaveAs));
        menu.Items.Add(new Separator());
        menu.Items.Add(BuildItem(L.T("card.remove"), () => _line.Remove(_item)));
        menu.Items.Add(BuildItem(L.T("card.discard"), () => _line.Discard(_item)));

        // 菜单弹出来的时候鼠标不在顶部了，别让绳子趁机收回去。
        menu.Opened += (_, _) => _line.HoldOpen(true);
        menu.Closed += (_, _) => _line.HoldOpen(false);
        menu.IsOpen = true;
    }

    private static MenuItem BuildItem(string header, Action action)
    {
        var menuItem = new MenuItem { Header = header };
        menuItem.Click += (_, _) => action();
        return menuItem;
    }

    private void CopyImage()
    {
        var image = ImageLoader.LoadFull(_item.FilePath);
        if (image is null)
        {
            Log.Note($"copy: cannot read {_item.FilePath}");
            return;
        }

        if (ClipboardPayload.Copy(image))
        {
            Log.Note($"copy ok {_item.FileName} {image.PixelWidth}x{image.PixelHeight}");
            FlashCopied();
        }
        else
        {
            Log.Note($"copy FAILED {_item.FileName}");
        }
    }

    /// 复制成功给个一眼能看见的反应。
    private void FlashCopied()
    {
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
        BorderThickness = new Thickness(2.4);

        _flashTimer?.Stop();
        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer?.Stop();
            BorderBrush = GlassBorder();
            BorderThickness = new Thickness(1.3);
        };
        _flashTimer.Start();
    }

    private void OpenFile()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_item.FilePath) { UseShellExecute = true });
            Log.Note($"open {_item.FileName}");
            return;
        }
        catch (Exception exception)
        {
            Log.Note("open failed, falling back to paint", exception);
        }

        // 没有默认看图程序就退到画图，至少能看见东西。
        EditInPaint();
    }

    /// 长按：丢进画图改。画图关掉之后把缩略图重读一遍。
    private void EditInPaint()
    {
        try
        {
            var start = new ProcessStartInfo("mspaint.exe", $"\"{_item.FilePath}\"")
            {
                UseShellExecute = true,
            };

            var process = Process.Start(start);
            if (process is null) return;

            var dispatcher = _line.Dispatcher;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => dispatcher.BeginInvoke(new Action(() => _line.Reload(_item)));
            Log.Note($"paint {_item.FileName}");
        }
        catch (Exception exception)
        {
            Log.Note("paint failed", exception);
        }
    }

    private void RevealInFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_item.FilePath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // 资源管理器打不开就算了。
        }
    }

    private void SaveAs()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = _item.FileName,
            Filter = L.T("dialog.imageFilter"),
            DefaultExt = System.IO.Path.GetExtension(_item.FileName),
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            System.IO.File.Copy(_item.FilePath, dialog.FileName, true);
            Log.Note($"saved as {dialog.FileName}");
        }
        catch (Exception exception)
        {
            Log.Note("save as failed", exception);
        }
    }
}
