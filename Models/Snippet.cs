using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Znip.Models;

public class Snippet : INotifyPropertyChanged
{
    private string _keyword = "";
    private string _label = "";
    private string _content = "";
    private Guid? _groupId;

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所属グループのId(nullなら未分類)</summary>
    public Guid? GroupId
    {
        get => _groupId;
        set { if (_groupId != value) { _groupId = value; OnPropertyChanged(nameof(GroupId)); } }
    }

    /// <summary>入力すると展開されるキーワード(例: ";addr")</summary>
    public string Keyword
    {
        get => _keyword;
        set { if (_keyword != value) { _keyword = value; OnPropertyChanged(nameof(Keyword)); OnPropertyChanged(nameof(DisplayName)); OnPropertyChanged(nameof(HasKeyword)); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Subtitle)); OnPropertyChanged(nameof(SortKey)); } }
    }

    /// <summary>表示用の名前(任意)</summary>
    public string Label
    {
        get => _label;
        set { if (_label != value) { _label = value; OnPropertyChanged(nameof(Label)); OnPropertyChanged(nameof(DisplayName)); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Subtitle)); OnPropertyChanged(nameof(SortKey)); } }
    }

    /// <summary>展開される本文。{date} {time} {clipboard} {cursor} などの変数を使用可能</summary>
    public string Content
    {
        get => _content;
        set
        {
            // 編集・インポート・JSON 読み込みのすべてで本文の改行を統一する。
            var normalized = value.ReplaceLineEndings("\r\n").Replace("\v", "\r\n");
            if (_content != normalized) { _content = normalized; OnPropertyChanged(nameof(Content)); OnPropertyChanged(nameof(Preview)); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Subtitle)); OnPropertyChanged(nameof(SortKey)); }
        }
    }

    /// <summary>キーワードを持つか(空ならリストのキーワード表示を隠す。自動展開の対象外)</summary>
    [JsonIgnore]
    public bool HasKeyword => !string.IsNullOrWhiteSpace(Keyword);

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Keyword : Label;

    [JsonIgnore]
    public string Preview => OneLine(Content);

    /// <summary>ピッカーで最後に使った日時。検索語が空のときに最近使った順で並べるのに使う</summary>
    public DateTime? LastUsed { get; set; }

    private static readonly char[] KeywordSymbols = ";:/\\.,!#$%&*+-=?@^_`|~'\"".ToCharArray();

    /// <summary>一覧の見出し。名前が空なら本文の1行目、それも空ならキーワード</summary>
    [JsonIgnore]
    public string Title
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Label)) return Label.Trim();
            var first = Lines(Content).FirstOrDefault() ?? "";
            if (first.Length > 0) return first.Length > 60 ? first[..60] + "…" : first;
            return HasKeyword ? Keyword : "(無題)";
        }
    }

    /// <summary>見出しの下に出す本文の抜粋。見出しに本文の1行目を使ったときは2行目以降だけにして重複させない</summary>
    [JsonIgnore]
    public string Subtitle => !string.IsNullOrWhiteSpace(Label) ? Preview : OneLine(Lines(Content).Skip(1));

    /// <summary>
    /// 一覧の並び順。キーワードを持つものを先に、先頭の記号を除いたキーワード順で並べ、
    /// キーワードの無いものは後ろに見出し順で並べる(見出しの先頭が記号だと順番が読めなくなるため)
    /// </summary>
    [JsonIgnore]
    public string SortKey => HasKeyword ? "0" + Keyword.Trim().TrimStart(KeywordSymbols) : "1" + Title;

    /// <summary>
    /// 空行を除いた本文の各行。CR / LF のほか、TextBlock が改行として扱う U+2028 などでも区切る
    /// (BeefText から取り込んだ本文に U+2028 が混ざっていることがあり、一覧の行が2段に崩れていた)
    /// </summary>
    private static IEnumerable<string> Lines(string text) =>
        text.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static readonly string[] LineBreaks = ["\r\n", "\r", "\n", "\u2028", "\u2029", "\u0085", "\v", "\f"];

    private static string OneLine(string text) => OneLine(Lines(text));

    private static string OneLine(IEnumerable<string> lines)
    {
        var oneLine = string.Join(" ⏎ ", lines);
        return oneLine.Length > 80 ? oneLine[..80] + "…" : oneLine;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
