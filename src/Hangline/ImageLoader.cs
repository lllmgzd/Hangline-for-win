using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hangline;

/// 一次解码读出来的东西：缩略图、原图尺寸、内容指纹。
internal sealed record LineImage(BitmapSource Thumbnail, byte[] Signature, int PixelWidth, int PixelHeight);

internal static class ImageLoader
{
    /// 指纹的采样格。格子越细越不容易把两张不同的图认成一张，
    /// 但要容忍同一张图经两条路解码后的细微偏差，所以不适合逐字节相等。
    private const int SignatureGrid = 16;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff",
    };

    public static bool IsSupportedImage(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    /// 缩略图最长边不超过 maxEdge，同时算好内容指纹。读不了返回 null。
    public static LineImage? LoadForLine(string path, int maxEdge)
    {
        var full = Decode(path);
        if (full is null) return null;

        var edge = Math.Max(full.PixelWidth, full.PixelHeight);
        BitmapSource thumbnail = full;
        if (edge > maxEdge && edge > 0)
        {
            var scaled = new TransformedBitmap(full, new ScaleTransform((double)maxEdge / edge, (double)maxEdge / edge));
            scaled.Freeze();
            thumbnail = scaled;
        }

        return new LineImage(thumbnail, Signature(full), full.PixelWidth, full.PixelHeight);
    }

    public static BitmapSource? LoadFull(string path) => Decode(path);

    /// 缩到 16×16 的灰度。同一张图不管从截图目录来还是从剪贴板来，指纹都该对得上。
    private static byte[] Signature(BitmapSource image)
    {
        if (image.PixelWidth <= 0 || image.PixelHeight <= 0) return Array.Empty<byte>();

        var scaled = new TransformedBitmap(
            image,
            new ScaleTransform((double)SignatureGrid / image.PixelWidth, (double)SignatureGrid / image.PixelHeight));

        // 用预乘格式取字节：带透明通道的图在剪贴板来回一趟之后，直通格式的 RGB 会有舍入偏差。
        var converted = new FormatConvertedBitmap(scaled, PixelFormats.Pbgra32, null, 0);

        var stride = SignatureGrid * 4;
        var buffer = new byte[stride * SignatureGrid];
        converted.CopyPixels(buffer, stride, 0);
        return buffer;
    }

    private static BitmapSource? Decode(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            // 文件还没写完、格式不认识、权限不够：都不算错，只是这次读不到。
            return null;
        }
    }

    public static DateTime CreationTime(string path)
    {
        try
        {
            var created = File.GetCreationTime(path);
            return created == default ? File.GetLastWriteTime(path) : created;
        }
        catch
        {
            return DateTime.Now;
        }
    }
}
