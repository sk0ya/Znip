using static Znip.Services.NativeMethods;

namespace Znip.Services;

/// <summary>
/// アクティブなアプリへテキストを送り込む。
/// クリップボード経由(Ctrl+V)で貼り付け、元のクリップボード内容は後で復元する。
/// UI スレッドから呼ぶこと(クリップボード操作のため)。
/// </summary>
public static class TextInjector
{
    /// <summary>展開済みテキストを貼り付ける。cursorOffsetFromEnd >= 0 なら貼り付け後に←キーでカーソルを戻す。</summary>
    public static async Task PasteAsync(string text, int cursorOffsetFromEnd = -1)
    {
        // 元のクリップボード内容(テキストのみ)を退避
        string? oldText = null;
        try { if (System.Windows.Clipboard.ContainsText()) oldText = System.Windows.Clipboard.GetText(); }
        catch { }

        if (!TrySetClipboardText(text))
            return;

        ReleaseModifierKeys();
        await Task.Delay(30);

        SendKeys(
            KeyInput(VK_CONTROL, up: false),
            KeyInput(VK_V, up: false),
            KeyInput(VK_V, up: true),
            KeyInput(VK_CONTROL, up: true));

        if (cursorOffsetFromEnd > 0)
        {
            await Task.Delay(80);
            var moves = new List<INPUT>();
            for (int i = 0; i < cursorOffsetFromEnd; i++)
            {
                moves.Add(KeyInput(VK_LEFT, up: false));
                moves.Add(KeyInput(VK_LEFT, up: true));
            }
            SendKeys(moves.ToArray());
        }

        // 貼り付けが完了してからクリップボードを復元
        await Task.Delay(300);
        if (oldText != null)
            TrySetClipboardText(oldText);
    }

    private static bool TrySetClipboardText(string text)
    {
        for (int i = 0; i < 4; i++)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch
            {
                Thread.Sleep(50); // 他プロセスがクリップボードをロック中
            }
        }
        return false;
    }

    /// <summary>ホットキーで押されたままの修飾キーが Ctrl+V に混ざらないよう解放する</summary>
    private static void ReleaseModifierKeys()
    {
        SendKeys(
            KeyInput(VK_CONTROL, up: true),
            KeyInput(VK_SHIFT, up: true),
            KeyInput(VK_MENU, up: true),
            KeyInput((ushort)VK_LWIN, up: true),
            KeyInput((ushort)VK_RWIN, up: true));
    }
}
