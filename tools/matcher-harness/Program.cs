using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Hangline;
using cv = OpenCvSharp;

namespace HanglineHarness;

/// 用合成页面验证 FrameMatcher：算出每步位移，再按位移把帧拼起来，
/// 最后跟原始整页逐像素比对。零差异才算过关。
internal static class Program
{
    private const int PageWidth = 900;
    private const int PageHeight = 4000;
    private const int FrameWidth = 800;
    private const int FrameHeight = 600;

    private static int _failures;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Log.Note("harness: start");

        DeployCheck();

        // 重复行是这个功能的原始痛点：列表、代码、表格里大片几乎一样的横条。
        Check("重复行页面（原始痛点）", BuildRepeatedLines(), new[] { 120, 120, 120, 150, 90, 130, 120 });
        Check("重复行页面 · 小步滚动", BuildRepeatedLines(), new[] { 25, 25, 25, 25, 25, 25, 25, 25 });
        Check("文字页面", BuildText(), new[] { 130, 130, 130, 130, 130, 130, 130, 130, 130 });
        Check("文字页面 · 大跨度滚动", BuildText(), new[] { 380, 380, 380, 380 });
        Check("文字页面 · 小步滚动", BuildText(), new[] { 20, 20, 40, 20, 20 });

        Console.WriteLine();
        if (_failures == 0)
        {
            Console.WriteLine("全部通过。");
            return 0;
        }

        Console.WriteLine($"{_failures} 项失败。");
        return 1;
    }

    /// 部署自检：模拟发布后的目录布局，确认原生库能被解析并真的加载起来。
    /// 这比"构建通过"有意义得多——OpenCvSharp 的托管程序集在找不到原生库时
    /// 编译期一切正常，只在运行时炸。
    private static void DeployCheck()
    {
        Console.WriteLine("=== 部署自检 ===");

        // 从可执行文件往上找到仓库根，别写死绝对路径。
        var dist = FindRepoRoot() is { } root ? System.IO.Path.Combine(root, "dist") : null;
        if (dist is null || !System.IO.Directory.Exists(dist))
        {
            Console.WriteLine("  跳过：还没跑过 build.ps1，dist 不存在");
            return;
        }

        var native = System.IO.Path.Combine(dist, "runtimes", "win-x64", "native");
        var externPath = System.IO.Path.Combine(native, "OpenCvSharpExtern.dll");
        Console.WriteLine($"  原生库存在：{System.IO.File.Exists(externPath)}  ({native})");

        var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        NativeLibrary.SetDllImportResolver(
            typeof(cv::Mat).Assembly,
            (name, assembly, path) =>
            {
                var candidate = System.IO.Path.Combine(native, name + ".dll");
                return System.IO.File.Exists(candidate)
                    ? NativeLibrary.Load(candidate)
                    : IntPtr.Zero;
            });

        try
        {
            // 真的调一次 OpenCV：能跑通就说明原生库加载成功。
            using var mat = new cv::Mat(4, 4, cv::MatType.CV_8UC1, new cv::Scalar(7));
            var mean = cv::Cv2.Mean(mat).Val0;
            Console.WriteLine($"  加载原生库并调用 OpenCV 成功：4x4 全 7 的均值 = {mean:F1}  (RID {rid})");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"  加载失败：{exception.GetType().Name} {exception.Message}");
            _failures++;
        }
    }

    /// 从当前程序集往上找到含 .git 的仓库根。
    private static string? FindRepoRoot()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (System.IO.Directory.Exists(System.IO.Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static void Check(string name, Bitmap page, int[] steps)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {name}：步长 [{string.Join(", ", steps)}] ===");

        var matcher = new FrameMatcher(FrameWidth, FrameHeight);
        var pageBuffer = ReadPixels(page);

        // 每帧在整页里的顶部行号，以及它相对上一帧测得的下移量。
        var tops = new List<int> { 0 };
        var scrolls = new List<int>();
        var frames = new List<byte[]> { Frame(pageBuffer, 0) };
        matcher.Remember(frames[0]);

        var top = 0;
        foreach (var step in steps)
        {
            top += step;
            if (top + FrameHeight > PageHeight) top = PageHeight - FrameHeight;

            var frame = Frame(pageBuffer, top);
            var scroll = matcher.Scroll(frame);

            if (scroll is null)
            {
                Console.WriteLine($"  step {step,4} -> 对不上");
                _failures++;
                continue;
            }

            var measured = (int)Math.Round(Math.Abs(scroll.Value), MidpointRounding.AwayFromZero);
            var ok = Math.Abs(measured - step) <= 1;
            if (!ok) _failures++;

            Console.WriteLine($"  step {step,4} -> 测得 {measured,4}  (原始 {scroll.Value,8:F2})  {(ok ? "OK" : "偏差过大")}");

            if (measured <= 0 || measured > FrameHeight) continue;

            tops.Add(top);
            scrolls.Add(measured);
            frames.Add(frame);
        }

        // 先确认取帧本身没走样：每一帧都应该和它对应的页面行逐字节相同。
        for (var i = 0; i < frames.Count; i++)
        {
            var mismatch = 0;
            for (var y = 0; y < FrameHeight; y++)
            {
                for (var x = 0; x < FrameWidth; x++)
                {
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var f = frames[i][(y * FrameWidth + x) * 4 + channel];
                        var p = pageBuffer[((tops[i] + y) * PageWidth + x) * 4 + channel];
                        if (f != p) mismatch++;
                    }
                }
            }

            if (mismatch > 0)
            {
                Console.WriteLine($"  取帧 {i}（页面第 {tops[i]} 行起）：{mismatch} 个通道与页面不符");
                _failures++;
            }
        }

        // 按测得的位移拼接：第 i 帧从画布的第 (前面位移之和) 行开始画。
        var canvas = new byte[PageWidth * 4 * PageHeight];
        var placed = 0;

        for (var i = 0; i < frames.Count; i++)
        {
            var fromRow = i == 0 ? 0 : FrameHeight - scrolls[i - 1];
            var count = i == 0 ? FrameHeight : scrolls[i - 1];
            if (count <= 0) continue;
            if (placed + count > PageHeight) count = PageHeight - placed;
            if (count <= 0) break;

            for (var y = 0; y < count; y++)
            {
                var source = (fromRow + y) * FrameWidth * 4;
                var target = (placed + y) * PageWidth * 4;
                System.Buffer.BlockCopy(frames[i], source, canvas, target, FrameWidth * 4);
            }

            placed += count;
        }

        // 跟原始整页比对。基准是页面第一帧的位置：画布第 0 行对应页面第 base 行。
        var expected = pageBuffer;
        var baseRow = tops[0];
        var diff = 0;
        var worst = 0;
        for (var y = 0; y < placed; y++)
        {
            var canvasOffset = y * PageWidth * 4;
            var pageOffset = (baseRow + y) * PageWidth * 4;
            for (var x = 0; x < FrameWidth; x++)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    var delta = Math.Abs(canvas[canvasOffset + x * 4 + channel] - expected[pageOffset + x * 4 + channel]);
                    if (delta > 0)
                    {
                        diff++;
                        if (delta > worst) worst = delta;
                    }
                }
            }
        }

        var pixels = (long)placed * FrameWidth * 3;
        var ratio = pixels == 0 ? 0 : diff * 100.0 / pixels;
        var clean = diff == 0;

        Console.WriteLine($"  拼出高度 {placed} / 每帧 {FrameHeight}，帧数 {frames.Count}");
        Console.WriteLine($"  与整页比对（页面第 {baseRow} 行起）：{diff} 个通道不同（{ratio:F3}%），最大偏差 {worst}  {(clean ? "逐像素一致" : "有接缝")}");
        if (!clean) _failures++;

        if (!clean)
        {
            DumpRows(canvas, expected, placed, baseRow);
        }
    }

    private static void DumpColumns(byte[] canvas, byte[] expected, int height, int first, int last)
    {
        var columns = new SortedDictionary<int, int>();
        for (var y = first; y <= last && y < height; y++)
        {
            var offset = y * PageWidth * 4;
            for (var x = 0; x < FrameWidth; x++)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    if (canvas[offset + x * 4 + channel] != expected[offset + x * 4 + channel])
                    {
                        columns.TryGetValue(x, out var count);
                        columns[x] = count + 1;
                    }
                }
            }
        }

        Console.WriteLine($"      {columns.Count} 个列有差异");
    }

    /// 差异按行统计，并统计每行是不是"整行错位"（那说明拼接位置错了，
    /// 而不是边缘几个像素的渲染差异）。
    private static void DumpRows(byte[] canvas, byte[] expected, int height, int baseRow)
    {
        var cleanRows = 0;
        var sharpRows = 0;
        var firstDiff = -1;
        var lastDiff = -1;

        for (var y = 0; y < height; y++)
        {
            var canvasOffset = y * PageWidth * 4;
            var pageOffset = (baseRow + y) * PageWidth * 4;
            var rowDiff = 0;
            for (var x = 0; x < FrameWidth; x++)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    if (canvas[canvasOffset + x * 4 + channel] != expected[pageOffset + x * 4 + channel]) rowDiff++;
                }
            }

            if (rowDiff == 0)
            {
                cleanRows++;
                continue;
            }

            if (firstDiff < 0) firstDiff = y;
            lastDiff = y;
            if (rowDiff > FrameWidth) sharpRows++;
        }

        Console.WriteLine($"    干净行 {cleanRows}/{height}；有差异的行落在 {firstDiff}..{lastDiff}；其中差异过半的行 {sharpRows}");
    }

    /// 从整页缓冲里裁一帧：直接按行拷贝，不走 GDI+ 绘制，避免引入渲染差异。
    private static byte[] Frame(byte[] page, int top)
    {
        var frame = new byte[FrameWidth * 4 * FrameHeight];
        for (var y = 0; y < FrameHeight; y++)
        {
            System.Buffer.BlockCopy(
                page,
                (top + y) * PageWidth * 4,
                frame,
                y * FrameWidth * 4,
                FrameWidth * 4);
        }

        return frame;
    }

    private static byte[] PageBuffer(Bitmap page) => ReadPixels(page);

    private static byte[] ReadPixels(Bitmap bitmap)
    {
        var locked = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        var buffer = new byte[bitmap.Width * 4 * bitmap.Height];
        Marshal.Copy(locked.Scan0, buffer, 0, buffer.Length);
        bitmap.UnlockBits(locked);
        return buffer;
    }

    /// 几乎一样的横条，只改最右边几个字符：左边 700 像素完全一致。
    /// 逐行签名在这种页面上会锁错位置，正是要验证的场景。
    private static Bitmap BuildRepeatedLines()
    {
        var page = NewPage();
        using var graphics = Graphics.FromImage(page);
        using var font = new Font("Consolas", 13f);
        graphics.Clear(Color.White);

        for (var i = 0; i < 200; i++)
        {
            var y = i * 20;
            graphics.FillRectangle(i % 2 == 0 ? Brushes.WhiteSmoke : Brushes.White, 0, y, PageWidth, 20);
            graphics.DrawString($"Line {i:D4}", font, Brushes.Black, 12, y + 2);
            graphics.DrawString($"{i % 7}", font, Brushes.DimGray, PageWidth - 40, y + 2);
        }

        return page;
    }

    private static Bitmap BuildText()
    {
        var page = NewPage();
        using var graphics = Graphics.FromImage(page);
        using var font = new Font("Microsoft YaHei", 12f);
        graphics.Clear(Color.White);

        var words = new[]
        {
            "长截图拼接要稳住位置", "特征点靠局部纹理投票", "重复行不会影响结果",
            "滚动一步大约一百二十像素", "接缝处不该出现重影", "页面停住就自动收工",
        };

        var random = new Random(20261016);
        for (var i = 0; i < 260; i++)
        {
            graphics.DrawString(words[random.Next(words.Length)] + " " + i, font, Brushes.Black, 20, i * 15 + 4);
        }

        return page;
    }

    private static Bitmap NewPage()
    {
        var page = new Bitmap(PageWidth, PageHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(page);
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        return page;
    }
}
