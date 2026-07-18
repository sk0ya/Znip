using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Znip.Models;
using Znip.Services;

namespace Znip.Views;

public partial class MainWindow : Window
{
    private SnippetStore Store => App.Current.Store;
    private ICollectionView _view = null!;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _view = CollectionViewSource.GetDefaultView(Store.Items);
        _view.Filter = FilterSnippet;
        SnippetList.ItemsSource = _view;

        if (SnippetList.Items.Count > 0)
            SnippetList.SelectedIndex = 0;

        Store.Saved += OnStoreSaved;

        // 設定タブの初期値
        var s = Store.Settings;
        HotkeyBox.Text = s.HotkeyDisplayText();
        AutoExpandCheck.IsChecked = s.AutoExpandEnabled;
        StartupCheck.IsChecked = StartupManager.IsEnabled();
        DataPathText.Text = SnippetStore.DataDirectory;
        HotkeyHintText.Text = s.HotkeyDisplayText();
        UpdateStatus();
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (SnippetsPanel == null || SettingsPanel == null) return;
        bool snippets = NavSnippets.IsChecked == true;
        SnippetsPanel.Visibility = snippets ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = snippets ? Visibility.Collapsed : Visibility.Visible;
        PageTitle.Text = snippets ? "スニペット" : "設定";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OnStoreSaved()
    {
        StatusText.Text = $"保存しました ({DateTime.Now:HH:mm:ss})  —  スニペット {Store.Items.Count} 件";
    }

    private void UpdateStatus()
    {
        StatusText.Text = $"スニペット {Store.Items.Count} 件(変更は自動保存されます)";
    }

    private bool FilterSnippet(object obj)
    {
        if (obj is not Snippet s) return false;
        var q = FilterBox.Text.Trim();
        if (q.Length == 0) return true;
        var cmp = StringComparison.OrdinalIgnoreCase;
        return s.Keyword.Contains(q, cmp) || s.Label.Contains(q, cmp) || s.Content.Contains(q, cmp);
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterHint.Visibility = FilterBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _view?.Refresh();
    }

    private void SnippetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = SnippetList.SelectedItem as Snippet;
        EditorPanel.DataContext = selected;
        EditorPanel.IsEnabled = selected != null;
        DuplicateButton.IsEnabled = selected != null;
        DeleteButton.IsEnabled = selected != null;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var snippet = new Snippet { Keyword = NextFreeKeyword(";new"), Label = "", Content = "" };
        Store.Items.Add(snippet);
        FilterBox.Text = "";
        SnippetList.SelectedItem = snippet;
        SnippetList.ScrollIntoView(snippet);
        KeywordBox.Focus();
        KeywordBox.SelectAll();
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (SnippetList.SelectedItem is not Snippet src) return;
        var copy = new Snippet
        {
            Keyword = NextFreeKeyword(src.Keyword),
            Label = src.Label.Length > 0 ? src.Label + " (コピー)" : "",
            Content = src.Content,
        };
        Store.Items.Add(copy);
        SnippetList.SelectedItem = copy;
        SnippetList.ScrollIntoView(copy);
    }

    private string NextFreeKeyword(string baseKeyword)
    {
        if (!Store.Items.Any(s => s.Keyword == baseKeyword)) return baseKeyword;
        for (int i = 2; ; i++)
        {
            var candidate = baseKeyword + i;
            if (!Store.Items.Any(s => s.Keyword == candidate)) return candidate;
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (SnippetList.SelectedItem is not Snippet snippet) return;
        var result = MessageBox.Show(
            $"「{snippet.DisplayName}」を削除しますか?", "Znip",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        int index = SnippetList.SelectedIndex;
        Store.Items.Remove(snippet);
        if (SnippetList.Items.Count > 0)
            SnippetList.SelectedIndex = Math.Min(index, SnippetList.Items.Count - 1);
        UpdateStatus();
    }

    private void InsertVariable_Click(object sender, RoutedEventArgs e)
    {
        if (EditorPanel.DataContext is not Snippet || sender is not Button btn || btn.Tag is not string variable)
            return;
        int caret = ContentBox.CaretIndex;
        ContentBox.Text = ContentBox.Text.Insert(caret, variable);
        ContentBox.CaretIndex = caret + variable.Length;
        ContentBox.Focus();
    }

    // ---- 設定タブ ----

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // 修飾キー単独はまだ組み合わせの途中
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            HotkeyStatus.Text = "修飾キーを押したまま、もう1つキーを押してください…";
            return;
        }

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None)
        {
            HotkeyStatus.Text = "Ctrl / Shift / Alt / Win のいずれかと組み合わせてください。";
            return;
        }

        var s = Store.Settings;
        var oldMods = s.HotkeyModifiers;
        var oldKey = s.HotkeyKey;
        s.HotkeyModifiers = mods;
        s.HotkeyKey = key;

        if (App.Current.Hotkeys.Register(mods, key))
        {
            HotkeyBox.Text = s.HotkeyDisplayText();
            HotkeyHintText.Text = s.HotkeyDisplayText();
            HotkeyStatus.Text = "ホットキーを変更しました。";
            Store.ScheduleSave();
        }
        else
        {
            // 競合したら元に戻す
            s.HotkeyModifiers = oldMods;
            s.HotkeyKey = oldKey;
            App.Current.Hotkeys.Register(oldMods, oldKey);
            HotkeyStatus.Text = "そのキーは他のアプリが使用中です。別の組み合わせを試してください。";
        }
    }

    private void AutoExpand_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool enabled = AutoExpandCheck.IsChecked == true;
        Store.Settings.AutoExpandEnabled = enabled;
        if (enabled) App.Current.Hook.Start();
        else App.Current.Hook.Stop();
        Store.ScheduleSave();
    }

    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        try
        {
            StartupManager.SetEnabled(StartupCheck.IsChecked == true);
            Store.Settings.LaunchAtStartup = StartupCheck.IsChecked == true;
            Store.ScheduleSave();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"スタートアップ設定を変更できませんでした。\n{ex.Message}", "Znip",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = SnippetStore.DataDirectory,
            UseShellExecute = true,
        });
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // ×で閉じてもアプリは終了せずトレイに常駐する
        e.Cancel = true;
        Store.SaveNow();
        Hide();
    }
}
