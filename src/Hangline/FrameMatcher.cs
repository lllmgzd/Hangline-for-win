using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using cv = OpenCvSharp;

namespace Hangline;

/// 相邻两帧的纵向位移。用 ORB 特征点投票，不比整行像素。
///
/// 逐行签名在大量重复行（列表、代码、表格）上会锁错位置，接缝就出重影；
/// 特征点靠局部纹理，重复行几乎不影响投票结果。这一层只做帧间位移计算，
/// 不碰画布，也不依赖窗口，方便单独验证。
internal sealed class FrameMatcher
{
    /// ORB 每帧取多少个特征点。
    public const int MaxFeatures = 1500;

    /// 两个特征点要算同一处，横向差不能超过这个值（像素）。
    public const double HorizontalTolerance = 100.0;

    /// 描述子距离（Hamming）上限，超过的不计票。
    public const int MaxDescriptorDistance = 80;

    /// 最佳/次佳距离之比超过它就算可疑匹配，丢掉。
    public const float RatioThreshold = 0.75f;

    /// 一个位移值至少要拿到这么多票才算数，挡掉纹理贫瘠时的噪声。
    public const int MinVotes = 8;

    /// 投票数占上帧匹配点数的比例，低于它认为两帧对不上。
    public const double MinVoteRate = 0.05;

    private readonly int _width;
    private readonly int _height;
    private readonly cv::ORB _orb;
    private readonly cv::BFMatcher _matcher;

    private byte[]? _frame;
    private cv::KeyPoint[]? _keyPoints;
    private cv::Mat? _descriptors;

    public FrameMatcher(int width, int height)
    {
        _width = width;
        _height = height;
        _orb = cv::ORB.Create(
            MaxFeatures,
            1.2f,
            8,
            31,
            0,
            2,
            cv::ORBScoreType.Harris,
            31,
            20);
        // 不做 crossCheck：用比值判别更稳，而且只留上帧到当前帧一个方向的点对。
        _matcher = new cv::BFMatcher(cv::NormTypes.Hamming, crossCheck: false);
    }

    /// 上一帧留下的明细，只给诊断用。
    public int LastFeatureCount => _keyPoints?.Length ?? 0;

    /// 记下这一帧作为下一次比对的基准。
    public void Remember(byte[] bgra)
    {
        cv::Mat? gray = null;
        var descriptors = new cv::Mat();

        try
        {
            gray = ToGray(bgra);
            _orb.DetectAndCompute(gray, null, out var keyPoints, descriptors);

            Clear();
            _frame = bgra;
            _keyPoints = keyPoints;
            _descriptors = descriptors;
        }
        catch (Exception exception)
        {
            descriptors.Dispose();
            Log.Note("frame matcher: cannot analyse frame", exception);
        }
        finally
        {
            gray?.Dispose();
        }
    }

    public void Clear()
    {
        _frame = null;
        _keyPoints = null;
        _descriptors?.Dispose();
        _descriptors = null;
    }

    /// 当前帧相对上一帧往下滚了多少行；对不上返回 null（没有基准帧也算对不上）。
    ///
    /// 返回 null 和"返回 0"是两件事：前者是配不上，后者是画面没动。
    public double? Scroll(byte[] bgra)
    {
        var previous = _descriptors;
        if (previous is null || _frame is null || _keyPoints is null) return null;
        if (_keyPoints.Length == 0 || previous.Empty()) return null;

        cv::Mat? gray = null;
        var descriptors = new cv::Mat();

        try
        {
            gray = ToGray(bgra);
            _orb.DetectAndCompute(gray, null, out var keyPoints, descriptors);
            if (keyPoints.Length == 0 || descriptors.Empty()) return null;

            var matches = _matcher.KnnMatch(descriptors, previous, 2);
            var scroll = Vote(keyPoints, matches);
            if (scroll is not null)
            {
                // 投票通过了才算这一帧可用，顺便把它变成新的基准。
                Clear();
                _frame = bgra;
                _keyPoints = keyPoints;
                _descriptors = descriptors;
                descriptors = null;
            }

            return scroll;
        }
        catch (Exception exception)
        {
            Log.Note("frame matcher: match failed", exception);
            return null;
        }
        finally
        {
            gray?.Dispose();
            descriptors?.Dispose();
        }
    }

    /// 按纵向位移分桶投票，票最多的那个位移就是滚动量。
    private double? Vote(cv::KeyPoint[] current, cv::DMatch[][] matches)
    {
        // 桶 -> (票数, 位移之和)。整数桶，桶内再取均值拿到亚像素位移。
        var votes = new Dictionary<int, (int Count, double Sum)>();
        var total = 0;

        foreach (var pair in matches)
        {
            if (pair.Length == 0) continue;
            var best = pair[0];

            // 比值判别：最佳和次佳差不多近，说明这个点没配准，丢掉。
            if (pair.Length > 1 && best.Distance > RatioThreshold * pair[1].Distance) continue;
            if (best.Distance > MaxDescriptorDistance) continue;

            var from = _keyPoints![best.TrainIdx];
            var to = current[best.QueryIdx];

            // 同一处纹理在横向上的位置不该跑偏，偏了就是错配。
            if (Math.Abs(to.Pt.X - from.Pt.X) > HorizontalTolerance) continue;

            // 上帧的点跑到新帧更低的位置 = 内容往上走了 = 页面往下滚了。
            var shift = to.Pt.Y - from.Pt.Y;
            total++;

            var bin = (int)Math.Round(shift, MidpointRounding.AwayFromZero);
            votes[bin] = votes.TryGetValue(bin, out var slot)
                ? (slot.Count + 1, slot.Sum + shift)
                : (1, shift);
        }

        if (total == 0) return null;

        // 取得票最多的桶。刻意不取最大的 key —— 位移最大的那个点往往是错配。
        var winner = 0;
        var winnerCount = 0;
        foreach (var entry in votes)
        {
            if (entry.Value.Count > winnerCount)
            {
                winnerCount = entry.Value.Count;
                winner = entry.Key;
            }
        }

        if (winnerCount < MinVotes) return null;
        if (winnerCount < total * MinVoteRate) return null;

        // 亚像素：只有邻近桶票数够份量时才参与平均，否则一个离群点就能把结果拉偏。
        var sum = 0.0;
        var count = 0;
        for (var bin = winner - 1; bin <= winner + 1; bin++)
        {
            if (!votes.TryGetValue(bin, out var slot)) continue;
            if (slot.Count * 2 < winnerCount) continue;
            sum += slot.Sum;
            count += slot.Count;
        }

        return count > 0 ? sum / count : votes[winner].Sum / winnerCount;
    }

    /// 把一帧 BGRA 转成灰度 Mat。屏幕每行是 width*4 字节，没有行间填充，
    /// 所以可以整行 Marshal.Copy 过去，不用逐像素写 Mat。
    private cv::Mat ToGray(byte[] frame)
    {
        var row = new byte[_width];
        var gray = new cv::Mat(_height, _width, cv::MatType.CV_8UC1);
        try
        {
            for (var y = 0; y < _height; y++)
            {
                var source = y * _width * 4;
                for (var x = 0; x < _width; x++)
                {
                    var pixel = source + x * 4;
                    // 屏幕是 BGRA：B 在最低位。灰度按 BT.601 加权，和 OpenCV 一致；
                    // 乘完再右移，避免 B+G+R 中间结果溢出 byte。
                    row[x] = (byte)((frame[pixel] * 29 + frame[pixel + 1] * 150 + frame[pixel + 2] * 77) >> 8);
                }

                Marshal.Copy(row, 0, gray.Data + y * _width, _width);
            }
        }
        catch
        {
            gray.Dispose();
            throw;
        }

        return gray;
    }
}
