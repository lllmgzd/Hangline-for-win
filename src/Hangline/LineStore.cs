using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace Hangline;

/// 绳子上挂着什么。
internal sealed class LineStore
{
    public ObservableCollection<PegItem> Items { get; } = new();

    public int MaxItems { get; set; } = 12;

    public event Action? Changed;

    public void Add(PegItem item)
    {
        // 同一个文件只挂一次，重复出现就挪到最新。
        var existing = Items.FirstOrDefault(i =>
            string.Equals(i.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) Items.Remove(existing);

        Items.Add(item);
        while (Items.Count > MaxItems) Items.RemoveAt(0);
        Changed?.Invoke();
    }

    public void Remove(PegItem item)
    {
        if (Items.Remove(item)) Changed?.Invoke();
    }

    /// 换掉某一张（比如在画图里改过之后重新读缩略图）。
    public void Replace(int index, PegItem item)
    {
        if (index < 0 || index >= Items.Count) return;
        Items[index] = item;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (Items.Count == 0) return;
        Items.Clear();
        Changed?.Invoke();
    }

    public bool Contains(string filePath) => Items.Any(i =>
        string.Equals(i.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

    /// 同一张图的另一份副本已经在绳子上。
    public bool ContainsSignature(byte[] signature)
    {
        if (signature.Length == 0) return false;

        foreach (var item in Items)
        {
            if (Similar(item.Signature, signature)) return true;
        }
        return false;
    }

    /// 同一张图经两条路（截图目录、剪贴板）解码后可能差一两个灰度级，
    /// 所以按平均差和最大差一起判，而不是逐字节相等。
    private static bool Similar(byte[] left, byte[] right)
    {
        if (left.Length == 0 || left.Length != right.Length) return false;

        var total = 0;
        var worst = 0;
        for (var i = 0; i < left.Length; i++)
        {
            var difference = Math.Abs(left[i] - right[i]);
            total += difference;
            if (difference > worst) worst = difference;
        }

        return worst <= 16 && total / left.Length <= 2;
    }
}
