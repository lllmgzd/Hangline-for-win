using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Windows.Threading;

namespace Hangline;

/// 盯着 Windows 存截图的目录。程序自己不截屏：你照旧用 Win+Shift+S
/// 或 PrtScn，东西一落盘，绳子就把它接住。
internal sealed class ScreenshotWatcher : IDisposable
{
    /// 文件是分几步写完的，等落稳了再读。
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(350);

    private readonly List<string> _folders;
    private readonly DispatcherTimer _timer;
    private readonly ConcurrentDictionary<string, DateTime> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = new();

    public ScreenshotWatcher(IReadOnlyList<string> folders)
    {
        _folders = new List<string>(folders);
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _timer.Tick += (_, _) => Flush();
    }

    public IReadOnlyList<string> Folders => _folders;

    public event Action<string>? NewImage;

    public void Start()
    {
        AdoptExisting();

        foreach (var folder in _folders)
        {
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                var watcher = new FileSystemWatcher(folder)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                };
                watcher.Created += OnChanged;
                watcher.Renamed += OnRenamed;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception exception)
            {
                Log.Note($"cannot watch {folder}", exception);
            }
        }

        _timer.Start();
    }

    /// 把已经在目录里的图收进来，挂上绳子。
    public IReadOnlyList<string> ListExisting()
    {
        var files = new List<string>();
        foreach (var folder in _folders)
        {
            try
            {
                foreach (var path in System.IO.Directory.EnumerateFiles(folder))
                {
                    if (ImageLoader.IsSupportedImage(path)) files.Add(path);
                }
            }
            catch
            {
                // 某个目录暂时读不了就跳过，别影响其它目录。
            }
        }
        return files;
    }

    public void Dispose()
    {
        _timer.Stop();
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnChanged;
            watcher.Renamed -= OnRenamed;
            watcher.Dispose();
        }
        _watchers.Clear();
    }

    private void AdoptExisting()
    {
        // 启动前就躺在那里的图不算新截的，第一次列目录时不挂上去。
        foreach (var path in ListExisting()) _known.Add(path);
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsCandidate(e.FullPath)) return;
        _pending[e.FullPath] = DateTime.UtcNow;
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (!IsCandidate(e.FullPath)) return;
        _pending[e.FullPath] = DateTime.UtcNow;
    }

    private static bool IsCandidate(string path) => ImageLoader.IsSupportedImage(path);

    private void Flush()
    {
        if (_pending.IsEmpty) return;

        var now = DateTime.UtcNow;
        foreach (var entry in _pending)
        {
            if (now - entry.Value < SettleDelay) continue;
            if (!_pending.TryRemove(entry.Key, out _)) continue;
            if (_known.Contains(entry.Key)) continue;
            if (!File.Exists(entry.Key)) continue;

            _known.Add(entry.Key);
            NewImage?.Invoke(entry.Key);
        }
    }
}
