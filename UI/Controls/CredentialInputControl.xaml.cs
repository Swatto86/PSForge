using System.Management.Automation;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using PSForge.ViewModels;

namespace PSForge.UI.Controls;

/// <summary>
/// Code-behind for CredentialInputControl.
/// Constructs a PSCredential from the username TextBox and password PasswordBox,
/// then stores it in the ParameterValueViewModel.Value property.
///
/// This code-behind is necessary because:
/// 1. PasswordBox does not support data binding (by WPF design, for security).
/// 2. PSCredential construction requires both username and SecureString password.
/// 3. We need to create the composite credential object when either field changes.
/// </summary>
public partial class CredentialInputControl : UserControl
{
    public CredentialInputControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Called when either the username or password changes.
    /// Builds a PSCredential from the current values and stores it
    /// in the ParameterValueViewModel.Value property.
    /// </summary>
    private void OnCredentialChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ParameterValueViewModel vm) return;

        var username = UsernameBox.Text;
        var password = PasswordBox.SecurePassword;

        if (string.IsNullOrWhiteSpace(username) && password.Length == 0)
        {
            vm.Value = null;
            return;
        }

        // SecurePassword returns a copy; make it read-only for defense in depth
        var secureCopy = password.Copy();
        secureCopy.MakeReadOnly();

        vm.Value = new PSCredential(username, secureCopy);
    }
}
