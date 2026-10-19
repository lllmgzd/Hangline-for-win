using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace Hangline;

/// 绳子上挂着的一张图。
internal sealed class PegItem
{
    public PegItem(
        string filePath,
        BitmapSource thumbnail,
        byte[] signature,
        DateTime created,
        bool temporary)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        Thumbnail = thumbnail;
        Signature = signature;
        Created = created;
        Temporary = temporary;
    }

    public string FilePath { get; }

    public string FileName { get; }

    public BitmapSource Thumbnail { get; }

    /// 内容指纹（16×16 灰度），用来认出同一张图的另一份副本。
    public byte[] Signature { get; }

    public DateTime Created { get; }

    /// 剪贴板接住的图和自己截的图存在我们自己目录里。
    public bool Temporary { get; }
}
