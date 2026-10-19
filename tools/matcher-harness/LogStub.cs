using System;

namespace Hangline;

/// 验证脚本里的日志桩：替代 src/Hangline/Log.cs（那份依赖数据目录和本地化）。
/// 只用来观察匹配器内部判断，不写文件。
internal static class Log
{
    public static void Note(string message) => Console.WriteLine($"    [log] {message}");

    public static void Note(string message, Exception exception) =>
        Note($"{message}: {exception.GetType().Name} {exception.Message}");
}
