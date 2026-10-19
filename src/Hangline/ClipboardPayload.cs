using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hangline;

/// 把一张图放进剪贴板。
///
/// 只放 CF_BITMAP 的话，聊天工具、浏览器、画图这些目标收不到；所以 PNG、DIB、
/// TIFF 都放一份，谁认哪个拿哪个。
internal static class ClipboardPayload
{
    private static DateTime _selfCopyUntil = DateTime.MinValue;

    /// 刚刚是我们自己往剪贴板里放的图。随后那条剪贴板通知就是回声，别再挂一张。
    public static bool IsSelfEcho => DateTime.UtcNow < _selfCopyUntil;

    public static bool Copy(BitmapSource image)
    {
        // 剪贴板可能正被别的程序占着，每次重来都要重新造数据（流被读过就不能再用）。
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(Build(image), copy: true);
                _selfCopyUntil = DateTime.UtcNow.AddSeconds(3);
                return true;
            }
            catch (Exception exception)
            {
                Log.Note($"clipboard attempt {attempt + 1} failed", exception);
                Thread.Sleep(60);
            }
        }

        return false;
    }

    private static DataObject Build(BitmapSource image)
    {
        var data = new DataObject();
        data.SetData(DataFormats.Bitmap, image);

        var png = Encode(image, new PngBitmapEncoder());
        if (png is not null) data.SetData("PNG", new MemoryStream(png, writable: false));

        var dib = ToDib(image);
        if (dib is not null) data.SetData(DataFormats.Dib, new MemoryStream(dib, writable: false));

        var tiff = Encode(image, new TiffBitmapEncoder());
        if (tiff is not null) data.SetData(DataFormats.Tiff, new MemoryStream(tiff, writable: false));

        return data;
    }

    private static byte[]? Encode(BitmapSource image, BitmapEncoder encoder)
    {
        try
        {
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

    /// BITMAPINFOHEADER + 自下而上的 BGRA 像素，也就是 CF_DIB。
    private static byte[]? ToDib(BitmapSource image)
    {
        try
        {
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var width = converted.PixelWidth;
            var height = converted.PixelHeight;
            if (width <= 0 || height <= 0) return null;

            var stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            const int headerSize = 40;
            var dib = new byte[headerSize + pixels.Length];

            WriteInt(dib, 0, headerSize);
            WriteInt(dib, 4, width);
            WriteInt(dib, 8, height);
            WriteShort(dib, 12, 1);
            WriteShort(dib, 14, 32);
            WriteInt(dib, 16, 0);
            WriteInt(dib, 20, stride * height);

            // BitmapSource 是自上而下，DIB 正高度要求自下而上，逐行翻转。
            for (var row = 0; row < height; row++)
            {
                Buffer.BlockCopy(pixels, row * stride, dib, headerSize + (height - 1 - row) * stride, stride);
            }

            return dib;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteInt(byte[] target, int offset, int value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
        target[offset + 2] = (byte)(value >> 16);
        target[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteShort(byte[] target, int offset, short value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
    }
}
