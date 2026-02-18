using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PSForge.Models;
using PSForge.ViewModels;

namespace PSForge;

/// <summary>
/// Code-behind for MainWindow.
/// Resolves the MainViewModel from DI and handles minimal UI events
/// that cannot be expressed purely in XAML bindings.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        // Resolve MainViewModel from the DI container configured in App.xaml.cs
        _viewModel = App.Services!.GetRequiredService<MainViewModel>();
        DataContext = _viewModel;

        // Discover available modules on startup, then auto-load if a module name was
        // passed as a CLI argument (e.g., PSForge.exe Microsoft.PowerShell.Management).
        Loaded += async (_, _) =>
        {
            await _viewModel.DiscoverModulesCommand.ExecuteAsync(null);

            // If a module name was passed via CLI, auto-select and load it
            if (!string.IsNullOrEmpty(App.StartupModuleName))
            {
                await _viewModel.AutoLoadModuleAsync(App.StartupModuleName);
            }
        };
    }

    /// <summary>
    /// Handles cmdlet item click in the TreeView.
    /// TreeView doesn't natively support Command binding on leaf items,
    /// so we use a MouseLeftButtonDown event handler to update the ViewModel.
    /// </summary>
    private void CmdletItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is CmdletInfo cmdlet)
        {
            _viewModel.SelectedCmdlet = cmdlet;
        }
    }

    /// <summary>
    /// Dismisses the error banner by clearing the ErrorMessage property.
    /// </summary>
    private void DismissError_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ErrorMessage = null;
    }

    /// <summary>
    /// Sets the output mode to Grid view.
    /// RadioButton Checked events are used because binding enums to RadioButtons
    /// requires a value converter, and direct code-behind is simpler here.
    /// Guard: event fires during InitializeComponent before _viewModel is assigned.
    /// </summary>
    private void OutputMode_Grid(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        _viewModel.OutputViewModel.SelectedOutputMode = OutputViewModel.OutputMode.Grid;
    }

    /// <summary>
    /// Sets the output mode to Text (Format-List) view.
    /// </summary>
    private void OutputMode_Text(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        _viewModel.OutputViewModel.SelectedOutputMode = OutputViewModel.OutputMode.Text;
    }

    /// <summary>
    /// Sets the output mode to JSON view.
    /// </summary>
    private void OutputMode_Json(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        _viewModel.OutputViewModel.SelectedOutputMode = OutputViewModel.OutputMode.Json;
    }

    /// <summary>
    /// Closes the parameter detail sidebar by deselecting the parameter.
    /// </summary>
    private void CloseParameterDetail_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.ActiveCmdletViewModel != null)
        {
            _viewModel.ActiveCmdletViewModel.SelectedParameter = null;
        }
    }
}
