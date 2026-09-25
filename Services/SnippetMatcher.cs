using System.Text;
using Znip.Models;

namespace Znip.Services;

/// <summary>
/// ピッカーと設定画面の検索で共通に使う、スニペットと検索語の一致判定。
/// <list type="bullet">
/// <item>空白区切りの複数語は AND(順不同)。「署名 会社」でどちらも含むものに絞る</item>
/// <item>大文字小文字・全角半角・ひらがなカタカナを区別しない(「めーる」で「メール」に当たる)</item>
/// <item>キーワード先頭の記号は無視して前方一致を見る(「addr」で「;addr」が最上位に来る)</item>
/// <item>どこにも部分一致しない語は、キーワードと見出しに対してだけ飛び飛びの一致を許す(「thx」→「thanks」)</item>
/// </list>
/// </summary>
public static class SnippetMatcher
{
    /// <summary>検索語を正規化して語に分ける。空なら空配列</summary>
    public static string[] ParseQuery(string query) =>
        Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>一致の強さ。0 なら不一致。terms が空なら常に 1</summary>
    public static int Score(Snippet s, string groupName, string[] terms)
    {
        if (terms.Length == 0) return 1;

        var keyword = Normalize(s.Keyword);
        var keywordCore = keyword.TrimStart(KeywordPrefixChars);
        var title = Normalize(s.Title);
        var group = Normalize(groupName);
        var content = Normalize(s.Content);

        int total = 0;
        foreach (var term in terms)
        {
            int score = TermScore(term, keyword, keywordCore, title, group, content);
            if (score == 0) return 0; // どれか1語でも当たらなければ除外
            total += score;
        }
        return total;
    }

    private static int TermScore(string term, string keyword, string keywordCore, string title, string group, string content)
    {
        if (keyword.Length > 0)
        {
            if (keyword == term || keywordCore == term) return 1000;
            if (keyword.StartsWith(term, StringComparison.Ordinal) ||
                keywordCore.StartsWith(term, StringComparison.Ordinal))
                return 800 - Math.Min(keywordCore.Length - term.Length, 50); // 短いキーワードほど上
        }
        if (title.StartsWith(term, StringComparison.Ordinal)) return 600;
        if (keyword.Contains(term, StringComparison.Ordinal)) return 500;
        if (IsAtWordStart(title, term)) return 450;
        if (title.Contains(term, StringComparison.Ordinal)) return 400;
        if (group.Contains(term, StringComparison.Ordinal)) return 250;
        if (content.Contains(term, StringComparison.Ordinal)) return 100;

        // 飛び飛びの一致は誤爆しやすいので、2文字以上の語をキーワードと見出しにだけ許す
        if (term.Length >= 2)
            return Math.Max(Subsequence(term, keywordCore), Subsequence(term, title));
        return 0;
    }

    /// <summary>単語の頭(先頭、または空白や記号の直後)で一致するか</summary>
    private static bool IsAtWordStart(string text, string term)
    {
        for (int i = text.IndexOf(term, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(term, i + 1, StringComparison.Ordinal))
        {
            if (i == 0 || !char.IsLetterOrDigit(text[i - 1])) return true;
        }
        return false;
    }

    /// <summary>term の文字が順番どおりに text に現れれば、詰まっているほど高い点(10〜60)</summary>
    private static int Subsequence(string term, string text)
    {
        int ti = 0, start = -1, end = -1;
        for (int i = 0; i < text.Length && ti < term.Length; i++)
        {
            if (text[i] != term[ti]) continue;
            if (ti == 0) start = i;
            end = i;
            ti++;
        }
        if (ti < term.Length) return 0;
        int gaps = end - start + 1 - term.Length;
        if (gaps > term.Length * 2) return 0; // 散らばりすぎは偶然の一致とみなす
        return Math.Max(10, 60 - gaps * 5 - start);
    }

    private static readonly char[] KeywordPrefixChars = ";:/\\.,!#$%&*+-=?@^_`|~'\"".ToCharArray();

    /// <summary>全角半角(NFKC)・大文字小文字・カタカナ→ひらがなを揃える</summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c);
        return sb.ToString();
    }
}
