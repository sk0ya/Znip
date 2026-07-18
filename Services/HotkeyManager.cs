using System.Windows.Input;
using System.Windows.Interop;
using static Znip.Services.NativeMethods;

namespace Znip.Services;

/// <summary>グローバルホットキーの登録。メッセージ専用の非表示ウィンドウで WM_HOTKEY を受ける。</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0xB00F;
    private readonly HwndSource _source;
    private bool _registered;

    public event Action? HotkeyPressed;

    public HotkeyManager()
    {
        var p = new HwndSourceParameters("ZnipHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            UsesPerPixelOpacity = false,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>登録に成功したら true。他アプリと競合すると false。</summary>
    public bool Register(ModifierKeys modifiers, Key key)
    {
        Unregister();
        // WPF の ModifierKeys と Win32 の MOD_* はビット値が一致している
        uint mods = (uint)modifiers | MOD_NOREPEAT;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        _registered = RegisterHotKey(_source.Handle, HotkeyId, mods, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.Dispose();
    }
}
