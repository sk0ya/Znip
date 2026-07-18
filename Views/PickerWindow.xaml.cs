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

    private void RefreshList()
    {
        var query = SearchBox.Text.Trim();
        List<Snippet> results;

        if (query.Length == 0)
        {
            results = _store.Items.ToList();
        }
        else
        {
            // キーワード前方一致 > キーワード部分一致 > 名前 > 本文 の順で並べる
            results = _store.Items
                .Select(s => (Snippet: s, Score: Score(s, query)))
                .Where(t => t.Score > 0)
                .OrderByDescending(t => t.Score)
                .Select(t => t.Snippet)
                .ToList();
        }

        ResultList.ItemsSource = results;
        EmptyText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (results.Count > 0)
            ResultList.SelectedIndex = 0;
    }

    private static int Score(Snippet s, string query)
    {
        var cmp = StringComparison.OrdinalIgnoreCase;
        if (s.Keyword.StartsWith(query, cmp)) return 400;
        if (s.Keyword.Contains(query, cmp)) return 300;
        if (s.Label.Contains(query, cmp)) return 200;
        if (s.Content.Contains(query, cmp)) return 100;
        return 0;
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
                bool copyOnly = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
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

    private void ResultList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => _ = CommitAsync(copyOnly: false);

    private async Task CommitAsync(bool copyOnly)
    {
        if (_committing || ResultList.SelectedItem is not Snippet snippet)
            return;
        _committing = true;

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
