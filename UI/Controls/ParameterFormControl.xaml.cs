using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PSForge.ViewModels;

namespace PSForge.UI.Controls;

/// <summary>
/// Code-behind for ParameterFormControl.
/// The dynamic form generation is handled entirely via XAML data binding
/// with the ParameterDataTemplateSelector. Click/focus events on parameter
/// items are handled here to update the SelectedParameter in CmdletViewModel.
/// </summary>
public partial class ParameterFormControl : UserControl
{
    public ParameterFormControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Handles GotFocus on a parameter item container.
    /// When a user tabs into or clicks a parameter's input control, 
    /// this selects it in the detail sidebar.
    /// </summary>
    private void ParameterItem_GotFocus(object sender, RoutedEventArgs e)
    {
        SelectParameter(sender);
    }

    /// <summary>
    /// Handles PreviewMouseLeftButtonDown on a parameter item container.
    /// Selects the parameter when any part of its row is clicked.
    /// Does NOT mark the event as handled — clicks still reach child controls.
    /// </summary>
    private void ParameterItem_Click(object sender, MouseButtonEventArgs e)
    {
        SelectParameter(sender);
    }

    /// <summary>
    /// Finds the ParameterValueViewModel from the clicked element and
    /// sets it as the selected parameter on the parent CmdletViewModel.
    /// </summary>
    private void SelectParameter(object sender)
    {
        if (sender is not FrameworkElement element) return;
        if (element.DataContext is not ParameterValueViewModel paramVm) return;
        if (DataContext is not CmdletViewModel cmdletVm) return;

        cmdletVm.SelectedParameter = paramVm;
    }
}
