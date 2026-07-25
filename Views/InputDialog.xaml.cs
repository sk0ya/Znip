using System.Windows;
using System.Windows.Input;

namespace Znip.Views;

/// <summary>グループ名の入力・変更用の簡易ダイアログ</summary>
public partial class InputDialog : Window
{
    public string InputText => NameBox.Text;

    public InputDialog(string title, string message, string initialValue = "")
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        NameBox.Text = initialValue;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void NameBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { DialogResult = true; e.Handled = true; }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
    }

    /// <summary>タイトルバーが無いので、カードをドラッグして動かせるようにする</summary>
    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
