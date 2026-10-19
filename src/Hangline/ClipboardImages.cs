using System;
using System.IO;
using System.Security.Cryptography;
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
