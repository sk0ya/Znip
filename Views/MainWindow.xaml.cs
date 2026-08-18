using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Znip.Models;
using Znip.Services;

namespace Znip.Views;

public partial class MainWindow : Window
{
    private SnippetStore Store => App.Current.Store;
    private ICollectionView _view = null!;
    private GroupFilterItem? _selectedGroupFilter;

    /// <summary>グループサイドバーの1行を表す。Group が null の場合は仮想項目(すべて/未分類)。</summary>
    private sealed record GroupFilterItem(string Name, SnippetGroup? Group, bool IsUngroupedFilter);

    private sealed class GroupOption
    {
        public Guid? Id { get; init; }
        public string Name { get; init; } = "";
    }

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        StateChanged += (_, _) => UpdateMaximizeGlyph();
    }

    /// <summary>最大化ボタンのグリフを状態に合わせて切り替える</summary>
    private void UpdateMaximizeGlyph()
    {
        bool max = WindowState == WindowState.Maximized;
        //  = 元に戻す,  = 最大化 (Segoe MDL2 Assets)
        MaximizeButton.Content = max ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = max ? "元のサイズに戻す" : "最大化";
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _view = CollectionViewSource.GetDefaultView(Store.Items);
        _view.Filter = FilterSnippet;
        SnippetList.ItemsSource = _view;

        RefreshGroupFilterItems();
        RefreshGroupComboOptions();

        if (SnippetList.Items.Count > 0)
            SnippetList.SelectedIndex = 0;

        Store.Saved += OnStoreSaved;

        // 設定タブの初期値
        var s = Store.Settings;
        HotkeyBox.Text = s.HotkeyDisplayText();
        HotkeyStatus.Text = "Ctrl / Shift / Alt / Win との組み合わせが使えます。";
        AutoExpandCheck.IsChecked = s.AutoExpandEnabled;
        StartupCheck.IsChecked = StartupManager.IsEnabled();
        DataPathText.Text = SnippetStore.DataDirectory;
        HotkeyHintText.Text = s.HotkeyDisplayText();
        UpdateNavPage();
        UpdateMaximizeGlyph();
        UpdateStatus();
        UpdateListChrome();
        UpdateEditor();
    }

    private void Nav_Checked(object sender, RoutedEventArgs e) => UpdateNavPage();

    /// <summary>ナビの選択に合わせてページを切り替える。XAML 読み込み中は各要素が未生成なので何もしない。</summary>
    private void UpdateNavPage()
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

    /// <summary>
    /// サイドバーの余白をドラッグしてウィンドウを移動する。
    /// ナビ項目(RadioButton)の上ではボタンがイベントを処理するため、ここには届かない。
    /// 上端 46px は WindowChrome のキャプション領域なので元から移動できる。
    /// </summary>
    private void Sidebar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        if (WindowState == WindowState.Maximized) return; // 最大化中の DragMove は例外になる
        DragMove();
    }

    private void OnStoreSaved()
    {
        StatusText.Text = $"スニペット {Store.Items.Count} 件";
        SaveIndicator.Text = $"保存しました ({DateTime.Now:HH:mm:ss})";
    }

    private void UpdateStatus()
    {
        StatusText.Text = $"スニペット {Store.Items.Count} 件";
        SaveIndicator.Text = "変更は自動保存されます";
    }

    /// <summary>一覧の件数表示と空状態の切り替え</summary>
    private void UpdateListChrome()
    {
        int shown = SnippetList.Items.Count;
        ListCountText.Text = shown > 0 ? $"{shown} 件" : "";

        bool searching = FilterBox.Text.Trim().Length > 0;
        ListEmptyPanel.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListEmptyText.Text = Store.Items.Count == 0
            ? "まだスニペットがありません。\n「＋ 新規」から作ってみてください。"
            : searching
                ? "検索に一致するスニペットがありません。"
                : "このグループにはスニペットがありません。";
    }

    private bool FilterSnippet(object obj)
    {
        if (obj is not Snippet s) return false;
        if (!MatchesGroupFilter(s)) return false;
        var q = FilterBox.Text.Trim();
        if (q.Length == 0) return true;
        var cmp = StringComparison.OrdinalIgnoreCase;
        return s.Keyword.Contains(q, cmp) || s.Label.Contains(q, cmp) || s.Content.Contains(q, cmp);
    }

    private bool MatchesGroupFilter(Snippet s)
    {
        if (_selectedGroupFilter == null) return true;
        if (_selectedGroupFilter.Group != null) return s.GroupId == _selectedGroupFilter.Group.Id;
        if (_selectedGroupFilter.IsUngroupedFilter) return s.GroupId == null;
        return true; // 「すべて」
    }

    // ---- グループ ----

    private void RefreshGroupFilterItems()
    {
        var items = new List<GroupFilterItem> { new("すべて", null, false) };
        items.AddRange(Store.Groups
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new GroupFilterItem(g.Name, g, false)));
        items.Add(new GroupFilterItem("未分類", null, true));

        var previouslySelected = _selectedGroupFilter;
        GroupList.ItemsSource = items;

        var toSelect = previouslySelected == null
            ? items[0]
            : items.FirstOrDefault(i => i.Group?.Id == previouslySelected.Group?.Id && i.IsUngroupedFilter == previouslySelected.IsUngroupedFilter)
              ?? items[0];
        GroupList.SelectedItem = toSelect;
    }

    private void RefreshGroupComboOptions()
    {
        var items = new List<GroupOption> { new() { Id = null, Name = "(未分類)" } };
        items.AddRange(Store.Groups
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new GroupOption { Id = g.Id, Name = g.Name }));
        GroupComboBox.ItemsSource = items;
    }

    private void GroupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedGroupFilter = GroupList.SelectedItem as GroupFilterItem;
        _view?.Refresh();
        UpdateListChrome();
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("新しいグループ", "グループ名を入力してください。") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var name = dialog.InputText.Trim();
        if (name.Length == 0) return;

        var group = new SnippetGroup { Name = name };
        Store.Groups.Add(group);
        RefreshGroupFilterItems();
        RefreshGroupComboOptions();

        var items = (List<GroupFilterItem>)GroupList.ItemsSource;
        GroupList.SelectedItem = items.FirstOrDefault(i => i.Group?.Id == group.Id);
    }

    private void RenameGroup_Click(object sender, RoutedEventArgs e)
    {
        if (GroupList.SelectedItem is not GroupFilterItem { Group: SnippetGroup group }) return;
        var dialog = new InputDialog("グループ名の変更", "新しい名前を入力してください。", group.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var name = dialog.InputText.Trim();
        if (name.Length == 0) return;

        group.Name = name;
        RefreshGroupFilterItems();
        RefreshGroupComboOptions();
    }

    private void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (GroupList.SelectedItem is not GroupFilterItem { Group: SnippetGroup group }) return;
        if (!ConfirmDialog.Ask(this, "グループの削除",
                $"グループ「{group.Name}」を削除します。\n所属するスニペットは未分類になります。", "削除する"))
            return;

        Store.RemoveGroup(group);
        RefreshGroupFilterItems();
        RefreshGroupComboOptions();
        UpdateListChrome();
    }

    private void GroupList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindAncestor<ListBoxItem>(d) is { } item)
            item.IsSelected = true;
    }

    private void GroupList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // 「すべて」「未分類」は仮想項目なので名前変更・削除の対象外
        if (GroupList.SelectedItem is not GroupFilterItem { Group: not null })
            e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null && d is not T) d = VisualTreeHelper.GetParent(d);
        return d as T;
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        bool empty = FilterBox.Text.Length == 0;
        FilterHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ClearFilterButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        _view?.Refresh();
        UpdateListChrome();
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        FilterBox.Clear();
        FilterBox.Focus();
    }

    private void SnippetList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateEditor();

    /// <summary>選択の有無に応じてエディタ / プレースホルダを切り替える</summary>
    private void UpdateEditor()
    {
        var selected = SnippetList.SelectedItem as Snippet;
        EditorPanel.DataContext = selected;
        EditorPanel.Visibility = selected != null ? Visibility.Visible : Visibility.Collapsed;
        EditorPlaceholder.Visibility = selected != null ? Visibility.Collapsed : Visibility.Visible;
        DuplicateButton.IsEnabled = selected != null;
        DeleteButton.IsEnabled = selected != null;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var snippet = new Snippet
        {
            Keyword = NextFreeKeyword(";new"),
            Label = "",
            Content = "",
            GroupId = _selectedGroupFilter?.Group?.Id,
        };
        Store.Items.Add(snippet);
        FilterBox.Text = "";
        SnippetList.SelectedItem = snippet;
        SnippetList.ScrollIntoView(snippet);
        UpdateListChrome();
        UpdateStatus();
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
            GroupId = src.GroupId,
        };
        Store.Items.Add(copy);
        SnippetList.SelectedItem = copy;
        SnippetList.ScrollIntoView(copy);
        UpdateListChrome();
        UpdateStatus();
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
        if (!ConfirmDialog.Ask(this, "スニペットの削除",
                $"「{snippet.DisplayName}」を削除します。\nこの操作は元に戻せません。", "削除する"))
            return;

        int index = SnippetList.SelectedIndex;
        Store.Items.Remove(snippet);
        if (SnippetList.Items.Count > 0)
            SnippetList.SelectedIndex = Math.Min(index, SnippetList.Items.Count - 1);
        UpdateEditor();
        UpdateListChrome();
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

    private void ImportBeefText_Click(object sender, RoutedEventArgs e)
    {
        var beeftextDefaultDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "beeftext.org", "Beeftext");
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "BeefText の comboList.json を選択",
            Filter = "BeefText コンボリスト (comboList.json)|comboList.json|JSON ファイル (*.json)|*.json|すべてのファイル (*.*)|*.*",
            InitialDirectory = System.IO.Directory.Exists(beeftextDefaultDir) ? beeftextDefaultDir : "",
        };
        if (dialog.ShowDialog(this) != true) return;

        App.Current.ImportFromBeefText(dialog.FileName);

        RefreshGroupFilterItems();
        RefreshGroupComboOptions();
        UpdateListChrome();
        UpdateStatus();

        // 取り込んだ設定を画面に反映
        var s = Store.Settings;
        HotkeyBox.Text = s.HotkeyDisplayText();
        HotkeyHintText.Text = s.HotkeyDisplayText();
        AutoExpandCheck.IsChecked = s.AutoExpandEnabled;
        StartupCheck.IsChecked = StartupManager.IsEnabled();
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
