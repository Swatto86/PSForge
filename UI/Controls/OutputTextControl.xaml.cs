using System.Windows.Controls;

namespace PSForge.UI.Controls;

/// <summary>
/// Code-behind for OutputTextControl.
/// All display logic is handled via XAML data binding to OutputViewModel.TextOutput.
/// </summary>
public partial class OutputTextControl : UserControl
{
    public OutputTextControl()
    {
        InitializeComponent();
    }
}
