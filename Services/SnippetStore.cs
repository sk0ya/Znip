using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Znip.Models;

namespace Znip.Services;

/// <summary>
/// スニペットと設定の永続化。%APPDATA%\Znip\ 配下に JSON で保存する。
/// UI スレッドから変更されると 600ms のデバウンス付きで自動保存される。
/// </summary>
public class SnippetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Znip");

    private static string SnippetsPath => Path.Combine(DataDirectory, "snippets.json");
    private static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public ObservableCollection<Snippet> Items { get; } = new();
    public AppSettings Settings { get; private set; } = new();

    /// <summary>フックスレッドから安全に読めるスナップショット(キーワードを持つもののみ)</summary>
    public volatile List<Snippet> Snapshot = new();

    private DispatcherTimer? _saveTimer;
    public event Action? Saved;

    public void Load()
    {
        Directory.CreateDirectory(DataDirectory);

        if (File.Exists(SettingsPath))
        {
            try { Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new(); }
            catch { Settings = new(); }
        }

        List<Snippet>? loaded = null;
        if (File.Exists(SnippetsPath))
        {
            try { loaded = JsonSerializer.Deserialize<List<Snippet>>(File.ReadAllText(SnippetsPath), JsonOptions); }
            catch { /* 壊れたファイルは無視(上書きは保存時) */ }
        }

        Items.Clear();
        foreach (var s in loaded ?? CreateSampleSnippets())
            Items.Add(s);

        RebuildSnapshot();

        foreach (var s in Items)
            s.PropertyChanged += OnItemChanged;
        Items.CollectionChanged += OnCollectionChanged;
    }

    private static List<Snippet> CreateSampleSnippets() => new()
    {
        new Snippet { Keyword = ";date", Label = "今日の日付", Content = "{date:yyyy/MM/dd}" },
        new Snippet { Keyword = ";now", Label = "日時", Content = "{date:yyyy/MM/dd} {time:HH:mm}" },
        new Snippet { Keyword = ";mail", Label = "メールアドレス", Content = "your-address@example.com" },
        new Snippet { Keyword = ";thx", Label = "お礼の定型文", Content = "お世話になっております。\nご対応いただきありがとうございます。\n引き続きよろしくお願いいたします。" },
        new Snippet { Keyword = ";sig", Label = "署名", Content = "――――――――――――――\n山田 太郎\nExample株式会社\n{cursor}\n――――――――――――――" },
    };

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (Snippet s in e.NewItems) s.PropertyChanged += OnItemChanged;
        if (e.OldItems != null)
            foreach (Snippet s in e.OldItems) s.PropertyChanged -= OnItemChanged;
        ScheduleSave();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => ScheduleSave();

    /// <summary>デバウンス付きの自動保存(600ms 変更が止まったら書き込み)</summary>
    public void ScheduleSave()
    {
        RebuildSnapshot();
        if (_saveTimer == null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _saveTimer.Tick += (_, _) => { _saveTimer!.Stop(); SaveNow(); };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            WriteAtomic(SnippetsPath, JsonSerializer.Serialize(Items.ToList(), JsonOptions));
            WriteAtomic(SettingsPath, JsonSerializer.Serialize(Settings, JsonOptions));
            Saved?.Invoke();
        }
        catch { /* 保存失敗は次回の変更時に再試行される */ }
    }

    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    private void RebuildSnapshot()
    {
        Snapshot = Items.Where(s => !string.IsNullOrWhiteSpace(s.Keyword)).ToList();
    }
}
