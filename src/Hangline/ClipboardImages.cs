using System;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hangline;

/// 剪贴板里被复制的图片。只有"纯图片"才接：带着文件或文字的不管，
/// 密码管理器之类标记为私密的也不管。
internal static class ClipboardImages
{
    private const int MaxBytes = 64 * 1024 * 1024;
    private const long MaxPixels = 80_000_000;

    private static string? _lastHash;

    public static string? TryCapture(string folder)
    {
        try
        {
            var data = System.Windows.Clipboard.GetDataObject();
            if (data is null) return null;

            var formats = data.GetFormats();
            if (formats.Length == 0) return null;

            // 连同文件或文字一起复制的，不接。
            if (Contains(formats, System.Windows.DataFormats.FileDrop)) return null;
            if (Contains(formats, System.Windows.DataFormats.Text)) return null;
            if (Contains(formats, System.Windows.DataFormats.UnicodeText)) return null;

            if (MarkedPrivate(data, formats)) return null;
            if (!HasImage(formats)) return null;

            var image = System.Windows.Clipboard.GetImage();
            if (image is null) return null;
            if ((long)image.PixelWidth * image.PixelHeight > MaxPixels) return null;

            // 有的程序（微信的截图工具就是）写剪贴板时没有填 alpha 通道，
            // 整张图 alpha 全是 0。WPF 把 alpha=0 当全透明渲染，挂到绳子上
            // 就只剩卡片底色，看起来是一块白板。颜色数据其实还在，补回来。
            image = RepairAlpha(image);

            var png = Encode(image);
            if (png is null || png.Length == 0 || png.Length > MaxBytes) return null;

            // 同一个图被重复放到剪贴板上，只挂一次。
            var hash = Convert.ToHexString(SHA256.HashData(png));
            if (string.Equals(hash, _lastHash, StringComparison.Ordinal)) return null;
            _lastHash = hash;

            System.IO.Directory.CreateDirectory(folder);
            var path = UniquePath(folder);
            File.WriteAllBytes(path, png);
            return path;
        }
        catch
        {
            // 剪贴板被别的程序占着，或格式读不动：跳过这一次。
            return null;
        }
    }

    /// 整张图 alpha 全是 0 时补成不透明。颜色数据本来就在，只是被 alpha 抹掉了。
    ///
    /// 不能简单地"只要 alpha 全 0 就补"：一张真正全透明的图补了会变成全黑。
    /// 所以要求 RGB 本身有内容（不是纯黑均匀色）才补。
    private static BitmapSource RepairAlpha(BitmapSource image)
    {
        try
        {
            var width = image.PixelWidth;
            var height = image.PixelHeight;
            if (width <= 0 || height <= 0) return image;

            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            var opaqueCount = 0;
            var firstRed = pixels[2];
            var firstGreen = pixels[1];
            var firstBlue = pixels[0];
            var rgbVaries = false;

            for (var i = 0; i + 3 < pixels.Length; i += 4)
            {
                if (pixels[i + 3] != 0) opaqueCount++;

                if (!rgbVaries
                    && (pixels[i] != firstBlue || pixels[i + 1] != firstGreen || pixels[i + 2] != firstRed))
                {
                    rgbVaries = true;
                }
            }

            // alpha 已经正常，或这张图本来就该全透明（RGB 也是一片纯色），都不动。
            if (opaqueCount > 0 || !rgbVaries) return image;

            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 0xFF;

            var repaired = BitmapSource.Create(
                width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            repaired.Freeze();
            return repaired;
        }
        catch
        {
            // 补不了就用原图，别把整次取图弄失败。
            return image;
        }
    }

    private static string UniquePath(string folder)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");
        var path = Path.Combine(folder, $"Clipboard {stamp}.png");
        var index = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(folder, $"Clipboard {stamp} {index}.png");
            index++;
        }
        return path;
    }

    private static byte[]? Encode(BitmapSource image)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static bool HasImage(string[] formats)
    {
        foreach (var format in formats)
        {
            if (string.Equals(format, System.Windows.DataFormats.Bitmap, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(format, "PNG", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(format, "DeviceIndependentBitmap", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(format, System.Windows.DataFormats.Tiff, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// Windows 上对应 macOS 那个"私密剪贴板"约定的写法。
    private static bool MarkedPrivate(System.Windows.IDataObject data, string[] formats)
    {
        if (Contains(formats, "ExcludeClipboardContentFromMonitorProcessing")) return true;

        foreach (var format in new[] { "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
        {
            if (!Contains(formats, format)) continue;
            try
            {
                if (data.GetData(format) is MemoryStream stream)
                {
                    var buffer = new byte[4];
                    if (stream.Read(buffer, 0, 4) == 4 && BitConverter.ToUInt32(buffer, 0) == 0) return true;
                }
            }
            catch
            {
                // 读不动就当没有这个标记。
            }
        }

        return false;
    }

    private static bool Contains(string[] formats, string format)
    {
        foreach (var candidate in formats)
        {
            if (string.Equals(candidate, format, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
