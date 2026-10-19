using System;
using System.Collections.Generic;
using System.Windows.Interop;

namespace Hangline;

/// 一个不显示的窗口，用来收全局快捷键和剪贴板通知的消息。
internal sealed class MessageWindow : IDisposable
{
    private const int LineHotkeyId = 0x484C;
    private const int ShotHotkeyId = 0x484D;

    private readonly HwndSource _source;
    private readonly HashSet<int> _registered = new();

    public MessageWindow()
    {
        var parameters = new HwndSourceParameters("HanglineMessageWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        Native.AddClipboardFormatListener(_source.Handle);
    }

    public IntPtr Handle => _source.Handle;

    /// 展开 / 收起绳子。
    public event Action? LineHotkeyPressed;

    /// 框选截图。
    public event Action? ShotHotkeyPressed;

    public event Action? ClipboardUpdated;

    public bool RegisterLineHotkey(uint modifiers, uint virtualKey) =>
        Register(LineHotkeyId, modifiers, virtualKey);

    public bool RegisterShotHotkey(uint modifiers, uint virtualKey) =>
        Register(ShotHotkeyId, modifiers, virtualKey);

    public void UnregisterHotkeys()
    {
        foreach (var id in _registered) Native.UnregisterHotKey(Handle, id);
        _registered.Clear();
    }

    public void Dispose()
    {
        UnregisterHotkeys();
        Native.RemoveClipboardFormatListener(_source.Handle);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private bool Register(int id, uint modifiers, uint virtualKey)
    {
        Native.UnregisterHotKey(Handle, id);
        _registered.Remove(id);

        if (!Native.RegisterHotKey(Handle, id, modifiers | Native.MOD_NOREPEAT, virtualKey)) return false;
        _registered.Add(id);
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (id == LineHotkeyId)
            {
                handled = true;
                LineHotkeyPressed?.Invoke();
            }
            else if (id == ShotHotkeyId)
            {
                handled = true;
                ShotHotkeyPressed?.Invoke();
            }
        }
        else if (msg == Native.WM_CLIPBOARDUPDATE)
        {
            handled = true;
            ClipboardUpdated?.Invoke();
        }

        return IntPtr.Zero;
    }
}
