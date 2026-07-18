using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Znip.Models;

public class Snippet : INotifyPropertyChanged
{
    private string _keyword = "";
    private string _label = "";
    private string _content = "";

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>入力すると展開されるキーワード(例: ";addr")</summary>
    public string Keyword
    {
        get => _keyword;
        set { if (_keyword != value) { _keyword = value; OnPropertyChanged(nameof(Keyword)); OnPropertyChanged(nameof(DisplayName)); } }
    }

    /// <summary>表示用の名前(任意)</summary>
    public string Label
    {
        get => _label;
        set { if (_label != value) { _label = value; OnPropertyChanged(nameof(Label)); OnPropertyChanged(nameof(DisplayName)); } }
    }

    /// <summary>展開される本文。{date} {time} {clipboard} {cursor} などの変数を使用可能</summary>
    public string Content
    {
        get => _content;
        set { if (_content != value) { _content = value; OnPropertyChanged(nameof(Content)); OnPropertyChanged(nameof(Preview)); } }
    }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Keyword : Label;

    [JsonIgnore]
    public string Preview
    {
        get
        {
            var oneLine = Content.Replace("\r\n", " ⏎ ").Replace("\n", " ⏎ ");
            return oneLine.Length > 80 ? oneLine[..80] + "…" : oneLine;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
