using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Znip.Models;
using Znip.Services;

namespace Znip.Views;

public partial class SnippetEditDialog : Window
{
    private readonly SnippetStore _store;
    private readonly Snippet _snippet;
    private sealed record GroupOption(Guid? Id, string Name);

    public SnippetEditDialog(SnippetStore store, Snippet snippet, bool isNew)
    {
        InitializeComponent();
        _store = store;
        _snippet = snippet;
        Title = isNew ? "スニペットの新規作成" : "スニペットの編集";
        // 入力は保存するまでモデルに反映しない。キャンセルや閉じる操作で元の値を保つ。
        NameBox.Text = snippet.Label;
        KeywordBox.Text = snippet.Keyword;
        BodyBox.Text = snippet.Content;
        var groups = new List<GroupOption> { new(null, "未分類") };
        groups.AddRange(store.Groups.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new GroupOption(g.Id, g.Name)));
        GroupBox.ItemsSource = groups;
        GroupBox.SelectedItem = groups.FirstOrDefault(g => g.Id == snippet.GroupId) ?? groups[0];
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var keyword = KeywordBox.Text.Trim();
        var duplicate = _store.Items.FirstOrDefault(s => s.Id != _snippet.Id && keyword.Length > 0 &&
            string.Equals(s.Keyword.Trim(), keyword, StringComparison.Ordinal));
        if (duplicate != null)
        {
            ErrorText.Text = $"このキーワードは「{duplicate.Title}」で使われています。別のキーワードにしてください。";
            ErrorText.Visibility = Visibility.Visible;
            KeywordBox.Focus();
            return;
        }
        _snippet.Label = NameBox.Text.Trim();
        _snippet.Keyword = keyword;
        _snippet.Content = BodyBox.Text;
        _snippet.GroupId = (GroupBox.SelectedItem as GroupOption)?.Id;
        DialogResult = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Save_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Variables_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void InsertVariable_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string variable }) return;
        int start = BodyBox.SelectionStart;
        BodyBox.SelectedText = variable;
        BodyBox.CaretIndex = start + variable.Length;
        BodyBox.Focus();
    }
}
