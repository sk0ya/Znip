using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Znip.Models;
using Znip.Services;

namespace Znip.Views;

/// <summary>
/// ホットキーで呼び出すスニペット選択ウィンドウ。
/// インクリメンタル検索 → Enter で元のアプリに貼り付け。
/// </summary>
public partial class PickerWindow : Window
{
    private readonly SnippetStore _store;
    private readonly IntPtr _targetWindow;
    private bool _committing;
    private bool _closing;
    private bool _activatedOnce;

    public PickerWindow(SnippetStore store, IntPtr targetWindow)
    {
        InitializeComponent();
        _store = store;
        _targetWindow = targetWindow;
        RefreshList();
        SourceInitialized += (_, _) => PositionNearCursor();
        Activated += (_, _) => _activatedOnce = true;
        Loaded += (_, _) =>
        {
            // ホットキー発火元が別プロセスでもフォーカスを確実に奪う
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            NativeMethods.ForceForeground(hwnd);
            Activate();
            SearchBox.Focus();
        };
    }

    /// <summary>Close() の再入(Deactivated 連鎖など)によるクラッシュを防ぐ</summary>
    private void CloseSafely()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        e.Handled = true;
        DragMove();
    }

    private void PositionNearCursor()
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(cursor);
        var wa = screen.WorkingArea;

        // デバイスピクセル → DIP 変換
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        // ウィンドウサイズ(DIP)を実測前なので想定値で計算
        double w = Width;
        double h = MaxHeight;

        var pos = transform.Transform(new Point(cursor.X, cursor.Y));
        var waTopLeft = transform.Transform(new Point(wa.Left, wa.Top));
        var waBottomRight = transform.Transform(new Point(wa.Right, wa.Bottom));

        Left = Math.Max(waTopLeft.X, Math.Min(pos.X - w / 2, waBottomRight.X - w));
        Top = Math.Max(waTopLeft.Y, Math.Min(pos.Y + 12, waBottomRight.Y - h));
    }

    /// <summary>結果リストの1行(グループ名はスニペット側に持っていないので引いておく)</summary>
    private sealed record PickerRow(Snippet Snippet, string GroupName);

    private void RefreshList()
    {
        var terms = SnippetMatcher.ParseQuery(SearchBox.Text);
        var groupNames = _store.Groups.ToDictionary(g => g.Id, g => g.Name);
        string GroupOf(Snippet s) => s.GroupId is Guid id && groupNames.TryGetValue(id, out var n) ? n : "未分類";

        // 検索語が空なら最近使った順。検索中は一致の強さ順で、同点なら最近使った順
        var results = _store.Items
            .Select(s => new { Row = new PickerRow(s, GroupOf(s)), Score = SnippetMatcher.Score(s, GroupOf(s), terms) })
            .Where(t => t.Score > 0)
            .OrderByDescending(t => t.Score)
            .ThenByDescending(t => t.Row.Snippet.LastUsed ?? DateTime.MinValue)
            .ThenBy(t => t.Row.Snippet.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(t => t.Row)
            .ToList();

        ResultList.ItemsSource = results;
        EmptyText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (results.Count > 0)
            ResultList.SelectedIndex = 0;
        UpdatePreview();
    }

    private void ResultList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdatePreview();

    /// <summary>下のプレビュー欄に選択中の本文を出す(変数は展開せずそのまま)</summary>
    private void UpdatePreview()
    {
        var content = (ResultList.SelectedItem as PickerRow)?.Snippet.Content.Trim('\r', '\n');
        PreviewPane.Visibility = string.IsNullOrEmpty(content) ? Visibility.Collapsed : Visibility.Visible;
        PreviewText.Text = content ?? "";
        PasteButton.IsEnabled = ResultList.SelectedItem is PickerRow;
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshList();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseSafely();
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                bool copyOnly = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || CopyButton.IsKeyboardFocused;
                _ = CommitAsync(copyOnly);
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        int count = ResultList.Items.Count;
        if (count == 0) return;
        int next = (ResultList.SelectedIndex + delta + count) % count;
        ResultList.SelectedIndex = next;
        ResultList.ScrollIntoView(ResultList.SelectedItem);
    }

    private async void Paste_Click(object sender, RoutedEventArgs e) => await CommitAsync(copyOnly: false);

    private async void Copy_Click(object sender, RoutedEventArgs e) => await CommitAsync(copyOnly: true);

    private async Task CommitAsync(bool copyOnly)
    {
        if (_committing || ResultList.SelectedItem is not PickerRow { Snippet: var snippet })
            return;
        _committing = true;

        snippet.LastUsed = DateTime.Now;
        _store.ScheduleSave();

        try
        {
            // クリップボード変数の展開はクリップボードを上書きする前に行う
            var expanded = TemplateEngine.Expand(snippet.Content);

            Hide();

            if (copyOnly)
            {
                try { System.Windows.Clipboard.SetDataObject(expanded.Text, true); } catch { }
            }
            else
            {
                if (_targetWindow != IntPtr.Zero)
                    NativeMethods.ForceForeground(_targetWindow);
                await Task.Delay(120); // フォーカスが戻るのを待つ
                await TextInjector.PasteAsync(expanded.Text, expanded.CursorOffsetFromEnd);
            }
        }
        finally
        {
            CloseSafely();
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        // 貼り付けのために自らフォーカスを手放した場合は閉じない(Commit 側で閉じる)。
        // ForceForeground によるウィンドウ表示直後は、まだ一度も Activated していない
        // 状態で見かけ上の Deactivated が飛んでくることがあるため無視する。
        if (!_committing && _activatedOnce)
            CloseSafely();
    }
}
