using System;
using System.IO;

namespace Hangline;

/// 出问题时有据可查：追加写到数据目录下的 hangline.log。
internal static class Log
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(AppSettings.DataFolder, "hangline.log");

    public static void Note(string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志写不下去也不能再抛。
        }
    }

    public static void Note(string message, Exception exception) =>
        Note($"{message}: {exception.GetType().Name} {exception.Message}");
}
