using System.Text.RegularExpressions;

namespace Znip.Services;

public record ExpandedTemplate(string Text, int CursorOffsetFromEnd);

/// <summary>
/// スニペット本文中の変数を展開する。
/// 対応: {date} {date:書式} {time} {time:書式} {clipboard} {cursor}
/// </summary>
public static partial class TemplateEngine
{
    [GeneratedRegex(@"\{(date|time)(?::([^{}]+))?\}", RegexOptions.IgnoreCase)]
    private static partial Regex DateTimePattern();

    public static ExpandedTemplate Expand(string content)
    {
        var now = DateTime.Now;
        var text = DateTimePattern().Replace(content, m =>
        {
            var kind = m.Groups[1].Value.ToLowerInvariant();
            var format = m.Groups[2].Success ? m.Groups[2].Value : (kind == "date" ? "yyyy/MM/dd" : "HH:mm");
            try { return now.ToString(format); }
            catch (FormatException) { return m.Value; }
        });

        if (text.Contains("{clipboard}", StringComparison.OrdinalIgnoreCase))
        {
            string clip = "";
            try { if (System.Windows.Clipboard.ContainsText()) clip = System.Windows.Clipboard.GetText(); }
            catch { /* クリップボードがロック中なら空文字 */ }
            text = Regex.Replace(text, @"\{clipboard\}", clip, RegexOptions.IgnoreCase);
        }

        // {cursor}: 貼り付け後にカーソルを置く位置(最初の1つのみ有効)
        int cursorOffset = -1;
        var idx = text.IndexOf("{cursor}", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var after = text[(idx + "{cursor}".Length)..];
            text = text.Remove(idx, "{cursor}".Length);
            // 矢印キーは \r\n を1回で移動するため改行は1文字として数える
            cursorOffset = after.Replace("\r\n", "\n").Length;
        }

        return new ExpandedTemplate(text, cursorOffset);
    }
}
