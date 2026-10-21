using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OMniX.WinUI.Views.Dialogs;

/// <summary>
///     压缩包密码输入对话框
/// </summary>
public sealed partial class ArchivePasswordDialog : ContentDialog
{
    public ArchivePasswordDialog(string archiveName, bool isRetry)
    {
        InitializeComponent();

        ArchiveNameText.Text = archiveName;
        WrongPasswordBar.IsOpen = isRetry;
        IsPrimaryButtonEnabled = false;

        Opened += (_, _) => PasswordInput.Focus(FocusState.Programmatic);
    }

    public string Password => PasswordInput.Password;

    private void PasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        IsPrimaryButtonEnabled = !string.IsNullOrEmpty(PasswordInput.Password);

    private void ShowPasswordCheckBox_OnChecked(object sender, RoutedEventArgs e) =>
        PasswordInput.PasswordRevealMode = PasswordRevealMode.Visible;

    private void ShowPasswordCheckBox_OnUnchecked(object sender, RoutedEventArgs e) =>
        PasswordInput.PasswordRevealMode = PasswordRevealMode.Hidden;
}
