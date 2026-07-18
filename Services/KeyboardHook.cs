using System.Text;
using System.Windows.Threading;
using Znip.Models;
using static Znip.Services.NativeMethods;

namespace Znip.Services;

/// <summary>
/// BeefText 風の自動展開。低レベルキーボードフックで入力を監視し、
/// キーワードが打ち終わった瞬間に Backspace で消して本文を貼り付ける。
/// ※ IME で変換中の文字は追跡できないため、キーワードは半角直接入力が前提。
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private readonly SnippetStore _store;
    private readonly Dispatcher _dispatcher;
    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc; // GC に回収されないよう保持

    private readonly StringBuilder _buffer = new(64);
    private long _lastInputTicks;
    private volatile bool _injecting;

    private const int MaxBuffer = 48;
    private static readonly TimeSpan IdleReset = TimeSpan.FromSeconds(5);

    public KeyboardHook(SnippetStore store, Dispatcher dispatcher)
    {
        _store = store;
        _dispatcher = dispatcher;
    }

    public bool IsRunning => _hookId != IntPtr.Zero;

    public void Start()
    {
        if (IsRunning) return;
        _proc = HookCallback;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        _proc = null;
        _buffer.Clear();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || _injecting)
            return CallNextHookEx(_hookId, nCode, wParam, lParam);

        int msg = wParam.ToInt32();
        if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
        {
            var info = System.Runtime.InteropServices.Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((info.flags & LLKHF_INJECTED) == 0)
                ProcessKeyDown(info);
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void ProcessKeyDown(KBDLLHOOKSTRUCT info)
    {
        // Ctrl / Alt / Win との組み合わせはショートカット → バッファをリセット
        if (IsDown(VK_CONTROL) || IsDown(VK_MENU) || IsDown(VK_LWIN) || IsDown(VK_RWIN))
        {
            _buffer.Clear();
            return;
        }

        // 一定時間入力がなければリセット
        var now = DateTime.UtcNow.Ticks;
        if (now - _lastInputTicks > IdleReset.Ticks)
            _buffer.Clear();
        _lastInputTicks = now;

        int vk = (int)info.vkCode;

        if (vk == VK_BACK)
        {
            if (_buffer.Length > 0) _buffer.Length--;
            return;
        }

        var c = TranslateToChar(info);
        if (c == null)
        {
            // 矢印・Enter・Esc など編集位置が変わるキーはリセット
            _buffer.Clear();
            return;
        }

        _buffer.Append(c.Value);
        if (_buffer.Length > MaxBuffer)
            _buffer.Remove(0, _buffer.Length - MaxBuffer);

        CheckForMatch();
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static char? TranslateToChar(KBDLLHOOKSTRUCT info)
    {
        var state = new byte[256];
        if (IsDown(VK_SHIFT)) state[VK_SHIFT] = 0x80;
        if ((GetKeyState(VK_CAPITAL) & 1) != 0) state[VK_CAPITAL] = 0x01;

        var sb = new StringBuilder(4);
        // wFlags bit2 (0x4): キーボード状態を変更しない (Win10 1607+)
        int rc = ToUnicode(info.vkCode, info.scanCode, state, sb, sb.Capacity, 0x4);
        if (rc <= 0 || sb.Length == 0) return null;
        var c = sb[^1];
        return char.IsControl(c) ? null : c;
    }

    private void CheckForMatch()
    {
        var snapshot = _store.Snapshot;
        var buffer = _buffer.ToString();

        foreach (var snippet in snapshot)
        {
            var kw = snippet.Keyword;
            if (kw.Length < 2 || !buffer.EndsWith(kw, StringComparison.Ordinal))
                continue;

            _buffer.Clear();
            var content = snippet.Content;
            var backspaces = kw.Length;
            _dispatcher.InvokeAsync(() => ExpandAsync(backspaces, content));
            return;
        }
    }

    private async Task ExpandAsync(int backspaces, string content)
    {
        _injecting = true;
        try
        {
            // キーワード最後の1文字がアプリに届くのを待つ
            await Task.Delay(50);

            var del = new List<INPUT>();
            for (int i = 0; i < backspaces; i++)
            {
                del.Add(KeyInput(VK_BACK, up: false));
                del.Add(KeyInput(VK_BACK, up: true));
            }
            NativeMethods.SendKeys(del.ToArray());
            await Task.Delay(50);

            var expanded = TemplateEngine.Expand(content);
            await TextInjector.PasteAsync(expanded.Text, expanded.CursorOffsetFromEnd);
        }
        finally
        {
            _injecting = false;
        }
    }

    public void Dispose() => Stop();
}
