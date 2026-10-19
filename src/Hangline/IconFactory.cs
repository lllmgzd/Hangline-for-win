using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hangline;

/// 托盘和窗口的图标也是画出来的，包里不带图片文件。
internal static class IconFactory
{
    public static BitmapSource CreateImage(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var ropeBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
            var ropePen = new Pen(ropeBrush, Math.Max(1.0, size / 16.0))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            dc.DrawLine(ropePen,
                new Point(size * 0.08, size * 0.26),
                new Point(size * 0.92, size * 0.30));

            DrawCard(dc, size, 0.14, 0.30, 0.26, 0.40, Color.FromRgb(0xE8, 0xF0, 0xFF), size);
            DrawCard(dc, size, 0.42, 0.34, 0.20, 0.30, Color.FromRgb(0xBF, 0xDB, 0xFE), size);
            DrawCard(dc, size, 0.66, 0.36, 0.22, 0.34, Color.FromRgb(0x93, 0xC5, 0xFD), size);
        }

        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    /// 写成 .ico 放数据目录，托盘的 NotifyIcon 要文件或句柄。
    public static string EnsureIconFile(int size = 32)
    {
        var folder = AppSettings.DataFolder;
        System.IO.Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, $"hangline-{size}.ico");
        if (File.Exists(path)) return path;

        var png = EncodePng(CreateImage(size));
        File.WriteAllBytes(path, WrapAsIco(png, size));
        return path;
    }

    private static void DrawCard(DrawingContext dc, double size, double x, double y, double w, double h, Color color, int iconSize)
    {
        var rect = new Rect(size * x, size * y, size * w, size * h);
        var radius = size * 0.07;
        var border = new Pen(new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0xAF)), Math.Max(0.7, iconSize / 34.0));
        dc.DrawRoundedRectangle(new SolidColorBrush(color), border, rect, radius, radius);
    }

    private static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// ICO 容器里直接塞一张 PNG，Vista 之后的 Windows 都认。
    private static byte[] WrapAsIco(byte[] png, int size)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(png.Length);
        writer.Write(22);
        writer.Write(png);
        writer.Flush();
        return stream.ToArray();
    }
}
