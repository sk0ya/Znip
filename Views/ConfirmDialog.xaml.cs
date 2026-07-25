using System.Windows;
using System.Windows.Input;

namespace Znip.Views;

/// <summary>削除など、取り消せない操作の確認ダイアログ(OS 標準の MessageBox の代わり)</summary>
public partial class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message, string confirmText)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        OkButton.Content = confirmText;
        Loaded += (_, _) => CancelButton.Focus();
    }

    /// <summary>確認して、実行してよければ true を返す。</summary>
    public static bool Ask(Window owner, string title, string message, string confirmText = "OK")
        => new ConfirmDialog(title, message, confirmText) { Owner = owner }.ShowDialog() == true;

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
