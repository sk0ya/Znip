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

    /// <summary>ホットキー欄が「記録中」かどうか。明示的にクリックされたときだけ true になる
    /// (フォーカスが当たっただけで押したキーを拾ってしまうと、誤って書き換わるため)</summary>
    private bool _recordingHotkey;

    /// <summary>グループペインの1行。Group が null の場合は仮想項目(すべて/未分類)。</summary>
    private sealed class GroupFilterItem : INotifyPropertyChanged
    {
        private int _count;

        public GroupFilterItem(string name, SnippetGroup? group, bool isUngroupedFilter)
        {
            Name = name;
            Group = group;
            IsUngroupedFilter = isUngroupedFilter;
        }

        public string Name { get; }
        public SnippetGroup? Group { get; }
        public bool IsUngroupedFilter { get; }

        /// <summary>名前変更・削除・ドロップの対象になる、ユーザーが作ったグループか</summary>
        public bool IsUserGroup => Group != null;

        /// <summary>行頭のアイコン(Segoe MDL2 Assets): すべて=一覧 / 未分類=トレイ / グループ=フォルダ</summary>
        public string Glyph => Group != null ? "\uE8B7" : IsUngroupedFilter ? "\uE7B8" : "\uE8FD";

        /// <summary>この行の下に区切り線を引くか(仮想項目と自分で作ったグループの境目)</summary>
        public bool HasDividerAfter { get; init; }

        /// <summary>この行に属するスニペットの件数(検索語は無視した総数)</summary>
        public int Count
        {
            get => _count;
            set { if (_count != value) { _count = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count))); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>設定ページの変数一覧の1行</summary>
    private sealed record VariableRow(string Name, string Description);

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
        // \uE923 = 元に戻す, \uE922 = 最大化 (Segoe MDL2 Assets)
        MaximizeButton.Content = max ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = max ? "元のサイズに戻す" : "最大化";
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _view = CollectionViewSource.GetDefaultView(Store.Items);
        _view.Filter = FilterSnippet;
        // 所属グループにかかわらず、キーワード順（キーワードなしは名前順）で並べる。
        if (_view is ListCollectionView lcv)
            lcv.CustomSort = Comparer<object>.Create((a, b) =>
                string.Compare(((Snippet)a).SortKey, ((Snippet)b).SortKey, StringComparison.CurrentCultureIgnoreCase));
        SnippetList.ItemsSource = _view;

        RefreshGroupFilterItems();

        if (SnippetList.Items.Count > 0)
            SnippetList.SelectedIndex = 0;

        Store.Saved += OnStoreSaved;

        VariableList.ItemsSource = new[]
        {
            new VariableRow("{date}", "今日の日付 (yyyy/MM/dd)"),
            new VariableRow("{date:yyyy年M月d日}", "書式を指定した日付"),
            new VariableRow("{time}", "現在時刻 (HH:mm)"),
            new VariableRow("{clipboard}", "クリップボードの内容"),
            new VariableRow("{cursor}", "貼り付け後にカーソルを置く位置"),
        };

        // 設定タブの初期値
        var s = Store.Settings;
        switch (s.Theme)
        {
            case AppTheme.Light: ThemeLightRadio.IsChecked = true; break;
            case AppTheme.Dark: ThemeDarkRadio.IsChecked = true; break;
            default: ThemeSystemRadio.IsChecked = true; break;
        }
        HotkeyBox.Text = s.HotkeyDisplayText();
        HotkeyStatus.Text = HotkeyHelpText;
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
        // 設定ページの間はサイドバーのグループの選択表示を消す(Tag をテンプレートのトリガーが見ている)
        GroupList.Tag = snippets ? null : "Inactive";
    }

    /// <summary>設定ページからでも、グループをクリックすればスニペットページへ戻る</summary>
    private void GroupList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        NavSnippets.IsChecked = true;

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OnStoreSaved()
    {
        SaveIndicator.Text = $"保存しました ({DateTime.Now:HH:mm:ss})";
    }

    private void UpdateStatus()
    {
        StatusText.Text = ""; // 件数は一覧の見出しに出しているので、ここは移動などの一時的な通知だけに使う
        SaveIndicator.Text = "ダブルクリックまたは Enter で編集";
    }

    /// <summary>空状態の切り替えとグループの件数更新</summary>
    private void UpdateListChrome()
    {
        int shown = SnippetList.Items.Count;
        RefreshGroupCounts();

        bool searching = FilterBox.Text.Trim().Length > 0;
        ListEmptyPanel.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListEmptyText.Text = Store.Items.Count == 0
            ? "まだスニペットがありません。\n「新規作成」から追加できます。"
            : searching
                ? "検索に一致するスニペットがありません。"
                : "このグループにはスニペットがありません。";
    }

    private bool FilterSnippet(object obj)
    {
        if (obj is not Snippet s) return false;
        if (!MatchesGroupFilter(s)) return false;
        var terms = SnippetMatcher.ParseQuery(FilterBox.Text);
        if (terms.Length == 0) return true;
        var groupName = s.GroupId is Guid id ? Store.Groups.FirstOrDefault(g => g.Id == id)?.Name ?? "" : "";
        return SnippetMatcher.Score(s, groupName, terms) > 0;
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
        // 仮想項目(すべて / 未分類)を上にまとめ、その下に自分で作ったグループを並べる
        var items = new List<GroupFilterItem>
        {
            new("すべて", null, false),
            new(UngroupedName, null, true) { HasDividerAfter = Store.Groups.Count > 0 },
        };
        items.AddRange(Store.Groups
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new GroupFilterItem(g.Name, g, false)));
        GroupHint.Visibility = Store.Groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var previouslySelected = _selectedGroupFilter;
        GroupList.ItemsSource = items;

        var toSelect = previouslySelected == null
            ? items[0]
            : items.FirstOrDefault(i => i.Group?.Id == previouslySelected.Group?.Id && i.IsUngroupedFilter == previouslySelected.IsUngroupedFilter)
              ?? items[0];
        GroupList.SelectedItem = toSelect;
        RefreshGroupCounts();
    }

    /// <summary>グループ行の右端に出す件数を数え直す(検索語では絞らない)</summary>
    private void RefreshGroupCounts()
    {
        if (GroupList.ItemsSource is not List<GroupFilterItem> items) return;
        foreach (var item in items)
        {
            item.Count = item.Group != null
                ? Store.Items.Count(s => s.GroupId == item.Group.Id)
                : item.IsUngroupedFilter
                    ? Store.Items.Count(s => s.GroupId == null)
                    : Store.Items.Count;
        }
    }

    private const string UngroupedName = "未分類";

    private string GroupNameOf(Guid? groupId) =>
        groupId is Guid id ? Store.Groups.FirstOrDefault(g => g.Id == id)?.Name ?? UngroupedName : UngroupedName;

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

        var items = (List<GroupFilterItem>)GroupList.ItemsSource;
        GroupList.SelectedItem = items.FirstOrDefault(i => i.Group?.Id == group.Id);
    }

    /// <summary>
    /// 操作対象のグループ。行のホバーボタンから呼ばれたときはその行(選択中とは限らない)、
    /// 右クリックメニューやキー操作からは選択中の行。
    /// </summary>
    private SnippetGroup? TargetGroup(object sender) =>
        ((sender as FrameworkElement)?.DataContext as GroupFilterItem ?? GroupList.SelectedItem as GroupFilterItem)?.Group;

    private void RenameGroup_Click(object sender, RoutedEventArgs e)
    {
        if (TargetGroup(sender) is not SnippetGroup group) return;
        var dialog = new InputDialog("グループ名の変更", "新しい名前を入力してください。", group.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var name = dialog.InputText.Trim();
        if (name.Length == 0) return;

        group.Name = name;
        RefreshGroupFilterItems();
    }

    private void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (TargetGroup(sender) is not SnippetGroup group) return;
        if (!ConfirmDialog.Ask(this, "グループの削除",
                $"グループ「{group.Name}」を削除します。\n所属するスニペットは未分類になります。", "削除する"))
            return;

        Store.RemoveGroup(group);
        RefreshGroupFilterItems();
        _view.Refresh();
        UpdateListChrome();
    }

    private void GroupList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 行のホバーボタン(名前変更 / 削除)をすばやく2回押したときは二重に動かさない
        if (e.OriginalSource is DependencyObject d && FindAncestor<ListBoxItem>(d) != null && FindAncestor<Button>(d) == null)
            RenameGroup_Click(GroupList, e);
    }

    private void GroupList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2) { RenameGroup_Click(GroupList, e); e.Handled = true; }
        else if (e.Key == Key.Delete) { DeleteGroup_Click(GroupList, e); e.Handled = true; }
    }

    // ---- スニペットをグループへドラッグ ----

    private Point? _dragStart;
    private ListBoxItem? _dropTarget;

    private void SnippetList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // スクロールバーを掴んだときはドラッグを始めない
        _dragStart = e.OriginalSource is DependencyObject d && FindAncestor<ListBoxItem>(d) != null
            ? e.GetPosition(SnippetList)
            : null;
    }

    private void SnippetList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(SnippetList) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragStart = null;
        if (SnippetList.SelectedItem is Snippet snippet)
            DragDrop.DoDragDrop(SnippetList, new DataObject(typeof(Snippet), snippet), DragDropEffects.Move);
        SetDropTarget(null);
    }

    /// <summary>ドロップ先のグループ行。「すべて」は移動先にならない</summary>
    private GroupFilterItem? DropTargetAt(DragEventArgs e, out ListBoxItem? container)
    {
        container = e.OriginalSource is DependencyObject d ? FindAncestor<ListBoxItem>(d) : null;
        return container?.DataContext is GroupFilterItem item && (item.Group != null || item.IsUngroupedFilter)
            ? item
            : null;
    }

    private void SetDropTarget(ListBoxItem? container)
    {
        if (_dropTarget == container) return;
        if (_dropTarget != null) _dropTarget.Tag = null;
        _dropTarget = container;
        if (_dropTarget != null) _dropTarget.Tag = "DropTarget";
    }

    private void GroupList_DragOver(object sender, DragEventArgs e)
    {
        ListBoxItem? container = null;
        var target = e.Data.GetDataPresent(typeof(Snippet)) ? DropTargetAt(e, out container) : null;
        SetDropTarget(target != null ? container : null);
        e.Effects = target != null ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void GroupList_DragLeave(object sender, DragEventArgs e) => SetDropTarget(null);

    private void GroupList_Drop(object sender, DragEventArgs e)
    {
        SetDropTarget(null);
        if (e.Data.GetData(typeof(Snippet)) is Snippet snippet && DropTargetAt(e, out _) is { } target)
            MoveToGroup(snippet, target.Group?.Id);
    }

    /// <summary>スニペットの所属を変える。今のフィルタから外れた場合は近くの行を選び直す</summary>
    private void MoveToGroup(Snippet snippet, Guid? groupId)
    {
        if (snippet.GroupId == groupId) return;
        int index = SnippetList.SelectedIndex;
        snippet.GroupId = groupId;
        _view.Refresh();
        if (SnippetList.Items.Contains(snippet))
            SnippetList.SelectedItem = snippet;
        else if (SnippetList.Items.Count > 0)
            SnippetList.SelectedIndex = Math.Clamp(index, 0, SnippetList.Items.Count - 1);
        UpdateEditor();
        UpdateListChrome();

        var name = groupId == null ? "未分類" : Store.Groups.FirstOrDefault(g => g.Id == groupId)?.Name;
        StatusText.Text = $"「{snippet.Title}」を {name} へ移動しました";
    }

    // ---- スニペット一覧の右クリック・キー操作 ----

    private void SnippetList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 右クリックした行を選択してからメニューを出す(選択中の別の行を消してしまわないように)
        if (e.OriginalSource is DependencyObject d && FindAncestor<ListBoxItem>(d) is { } item)
            item.IsSelected = true;
    }

    private void SnippetList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (SnippetList.SelectedItem is not Snippet snippet || SnippetList.ContextMenu is not ContextMenu menu)
        {
            e.Handled = true;
            return;
        }

        menu.Items.Clear();
        menu.Items.Add(MakeMenuItem("編集 (Enter)", Edit_Click));
        menu.Items.Add(MakeMenuItem("複製 (Ctrl+D)", Duplicate_Click));
        menu.Items.Add(MakeMenuItem("削除 (Delete)", Delete_Click));
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "グループへ移動", IsEnabled = false });

        var targets = new List<(string Name, Guid? Id)> { ("未分類", null) };
        targets.AddRange(Store.Groups
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => (g.Name, (Guid?)g.Id)));
        foreach (var (name, id) in targets)
        {
            bool current = snippet.GroupId == id;
            var item = MakeMenuItem((current ? "✓  " : "     ") + name, (_, _) => MoveToGroup(snippet, id));
            item.IsEnabled = !current;
            menu.Items.Add(item);
        }
    }

    private static MenuItem MakeMenuItem(string header, RoutedEventHandler click)
    {
        var item = new MenuItem { Header = header };
        item.Click += click;
        return item;
    }

    private void SnippetList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { Delete_Click(SnippetList, e); e.Handled = true; }
        else if (e.Key == Key.Enter) { Edit_Click(sender, e); e.Handled = true; }
    }

    private void FilterBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && FilterBox.Text.Length > 0)
        {
            FilterBox.Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && SnippetList.Items.Count > 0)
        {
            // 検索欄から ↓ で一覧へ
            if (SnippetList.SelectedIndex < 0) SnippetList.SelectedIndex = 0;
            FocusSelectedSnippet();
            e.Handled = true;
        }
    }

    private void FocusSelectedSnippet()
    {
        SnippetList.ScrollIntoView(SnippetList.SelectedItem);
        SnippetList.UpdateLayout();
        (SnippetList.ItemContainerGenerator.ContainerFromItem(SnippetList.SelectedItem) as ListBoxItem)?.Focus();
    }

    /// <summary>スニペットページのショートカット(Ctrl+N 新規 / Ctrl+F 検索 / Ctrl+D 複製)</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || _recordingHotkey) return;
        switch (e.Key)
        {
            case Key.N:
                New_Click(this, e);
                e.Handled = true;
                break;
            case Key.F:
                NavSnippets.IsChecked = true;
                FilterBox.Focus();
                FilterBox.SelectAll();
                e.Handled = true;
                break;
            case Key.D when NavSnippets.IsChecked == true:
                Duplicate_Click(this, e);
                e.Handled = true;
                break;
        }
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

    private void UpdateEditor()
    {
        bool selected = SnippetList.SelectedItem is Snippet;
        EditButton.IsEnabled = selected;
        DuplicateButton.IsEnabled = selected;
        DeleteButton.IsEnabled = selected;
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (SnippetList.SelectedItem is Snippet snippet) EditSnippet(snippet, false);
    }

    private void SnippetList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindAncestor<ListBoxItem>(d) != null)
            Edit_Click(sender, e);
    }

    private void EditSnippet(Snippet snippet, bool isNew)
    {
        var dialog = new SnippetEditDialog(Store, snippet, isNew) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (isNew) Store.Items.Add(snippet);
        // 保存後は所属先を表示し、検索条件で編集した行が消えないようにする。
        FilterBox.Clear();
        if (!MatchesGroupFilter(snippet) && GroupList.ItemsSource is List<GroupFilterItem> groups)
            GroupList.SelectedItem = groups.FirstOrDefault(g => g.Group?.Id == snippet.GroupId &&
                (snippet.GroupId != null || g.IsUngroupedFilter));
        _view.Refresh();
        SnippetList.SelectedItem = snippet;
        SnippetList.ScrollIntoView(snippet);
        Store.SaveNow();
        UpdateListChrome();
        UpdateEditor();
        FocusSelectedSnippet();
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        NavSnippets.IsChecked = true;
        EditSnippet(new Snippet { GroupId = _selectedGroupFilter?.Group?.Id }, true);
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (SnippetList.SelectedItem is not Snippet src) return;
        EditSnippet(new Snippet
        {
            Label = src.Title + " (コピー)",
            Content = src.Content,
            GroupId = src.GroupId,
        }, true);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (SnippetList.SelectedItem is not Snippet snippet) return;
        if (!ConfirmDialog.Ask(this, "スニペットの削除",
                $"「{snippet.Title}」を削除します。\nこの操作は元に戻せません。", "削除する"))
            return;

        int index = SnippetList.SelectedIndex;
        Store.Items.Remove(snippet);
        if (SnippetList.Items.Count > 0)
            SnippetList.SelectedIndex = Math.Min(index, SnippetList.Items.Count - 1);
        UpdateEditor();
        UpdateListChrome();
        UpdateStatus();

        // 一覧で Delete を押したときは、続けて操作できるよう次の行にフォーカスを残す
        if (sender == SnippetList && SnippetList.SelectedItem != null)
            FocusSelectedSnippet();
    }

    // ---- 設定タブ ----

    private const string HotkeyHelpText = "右の欄をクリックしてからキーを押すと変更できます(Ctrl / Shift / Alt / Win との組み合わせ)。";

    /// <summary>クリックされて初めて記録を始める</summary>
    private void HotkeyBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        StartRecordingHotkey();
    }

    private void HotkeyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_recordingHotkey) return;
        _recordingHotkey = false;
        HotkeyStatus.Text = HotkeyHelpText;
    }

    private void StartRecordingHotkey()
    {
        _recordingHotkey = true;
        HotkeyStatus.Text = "使いたいキーの組み合わせを押してください(Esc で取り消し)。";
        HotkeyBox.Focus();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        // 記録中でなければ何も変えない。Space / Enter で記録を開始できる(キーボード操作用)
        if (!_recordingHotkey)
        {
            if (e.Key is Key.Space or Key.Enter) StartRecordingHotkey();
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            _recordingHotkey = false;
            HotkeyStatus.Text = HotkeyHelpText;
            Keyboard.ClearFocus();
            return;
        }

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
            _recordingHotkey = false;
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

    private void Theme_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var theme = ThemeLightRadio.IsChecked == true ? AppTheme.Light
                  : ThemeDarkRadio.IsChecked == true ? AppTheme.Dark
                  : AppTheme.System;
        if (Store.Settings.Theme == theme) return;
        Store.Settings.Theme = theme;
        ThemeManager.Apply(theme);
        Store.ScheduleSave();
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
