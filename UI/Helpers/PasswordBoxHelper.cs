using System.Security;
using System.Windows;
using System.Windows.Controls;
using PSForge.ViewModels;

namespace PSForge.UI.Helpers;

/// <summary>
/// Attached property helper for PasswordBox binding to ParameterValueViewModel.
///
/// WPF's PasswordBox intentionally does not support data binding on the Password
/// property — this is a security measure to prevent passwords from appearing in
/// binding diagnostics, memory dumps, or XAML serialization.
///
/// This helper bridges the gap by listening to PasswordChanged events and updating
/// the ParameterValueViewModel.Value with a SecureString. The password never
/// exists as a plain string in our code.
/// </summary>
public static class PasswordBoxHelper
{
    /// <summary>
    /// Attached property: when set to true, hooks the PasswordChanged event
    /// to synchronize the password into the DataContext's Value property.
    /// </summary>
    public static readonly DependencyProperty AttachProperty =
        DependencyProperty.RegisterAttached(
            "Attach",
            typeof(bool),
            typeof(PasswordBoxHelper),
            new PropertyMetadata(false, OnAttachChanged));

    public static bool GetAttach(DependencyObject dp) => (bool)dp.GetValue(AttachProperty);
    public static void SetAttach(DependencyObject dp, bool value) => dp.SetValue(AttachProperty, value);

    private static void OnAttachChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox passwordBox) return;

        if ((bool)e.NewValue)
        {
            passwordBox.PasswordChanged += PasswordBox_PasswordChanged;
        }
        else
        {
            passwordBox.PasswordChanged -= PasswordBox_PasswordChanged;
        }
    }

    /// <summary>
    /// Converts the PasswordBox content to a SecureString and stores it in the
    /// ParameterValueViewModel.Value property. The SecureString is made read-only
    /// immediately to prevent further modification (defense in depth).
    /// </summary>
    private static void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox passwordBox) return;

        if (passwordBox.DataContext is ParameterValueViewModel vm)
        {
            var secureString = new SecureString();
            foreach (var c in passwordBox.Password)
            {
                secureString.AppendChar(c);
            }
            secureString.MakeReadOnly();
            vm.Value = secureString;
        }
    }
}
