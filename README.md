# PSForge

A modern Windows GUI application for executing and managing PowerShell commands with an intuitive interface.

## Overview

PSForge provides a user-friendly graphical interface for PowerShell scripting and command execution. It enables users to discover available cmdlets, explore module information, and execute commands with parameter validation and credential management—all without leaving the GUI.

## Features

- **Module Discovery**: Browse and discover installed PowerShell modules and cmdlets
- **Cmdlet Inspector**: View detailed cmdlet information including parameters and help text
- **Command Execution**: Execute PowerShell commands with parameter validation
- **Output Formatting**: Display results in grid or text format
- **Credential Management**: Securely input credentials for commands that require authentication
- **Command Preview**: Preview commands before execution
- **Multi-language Support**: Support for multiple UI languages

## Requirements

- **.NET 8.0** or higher
- **Windows 7 SP1** or higher (for WPF support)
- **PowerShell 5.0** or higher (for Core functionality)

## Building

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 or later (recommended) or VS Code with C# extension

### Build Instructions

```bash
# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Build in Release configuration
dotnet build -c Release
```

### Output Directories

- **Debug builds**: `bin/Debug/net8.0-windows/`
- **Release builds**: `bin/Release/net8.0-windows/`

## Running

### From Command Line

```bash
# After building
./bin/Debug/net8.0-windows/PSForge.exe
```

### From Visual Studio

- Open `PSForge.csproj` in Visual Studio 2022
- Press `F5` to build and run

## Project Structure

```
PSForge/
├── Core/                          # Core business logic
│   ├── CommandExecutor.cs         # Command execution engine
│   ├── ModuleIntrospector.cs      # PowerShell module reflection
│   └── PowerShellSessionManager.cs # PowerShell session management
│
├── Models/                        # Data models
│   ├── CmdletInfo.cs             # Cmdlet metadata
│   ├── ExecutionResult.cs        # Command execution results
│   ├── ModuleInfo.cs             # Module information
│   ├── ParameterInfo.cs          # Parameter details
│   └── ParameterSetInfo.cs       # Parameter set information
│
├── Services/                      # Application services
│   ├── HelpTextService.cs        # PowerShell help text retrieval
│   ├── ModuleDiscoveryService.cs # Module discovery
│   └── OutputFormatterService.cs # Result formatting
│
├── ViewModels/                    # MVVM ViewModels
│   ├── MainViewModel.cs          # Main application logic
│   ├── CmdletViewModel.cs        # Cmdlet browsing logic
│   ├── OutputViewModel.cs        # Output display logic
│   └── ParameterValueViewModel.cs # Parameter input logic
│
├── UI/                            # User interface
│   ├── Controls/                  # Custom XAML controls
│   │   ├── CommandPreviewControl.xaml
│   │   ├── CredentialInputControl.xaml
│   │   ├── OutputGridControl.xaml
│   │   ├── OutputTextControl.xaml
│   │   └── ParameterFormControl.xaml
│   ├── Converters/               # Value converters
│   ├── Styles/                   # XAML styles and templates
│   ├── Helpers/                  # UI helper utilities
│   └── TemplateSelectors/        # Dynamic template selection
│
├── App.xaml                       # Application configuration
├── MainWindow.xaml                # Main window UI
└── PSForge.csproj                # Project file
```

## Architecture

PSForge follows the **MVVM (Model-View-ViewModel)** pattern:

- **Models**: Represent PowerShell entities (cmdlets, modules, parameters)
- **ViewModels**: Handle business logic and state management
- **Views**: XAML-based UI components
- **Services**: Core functionality (module discovery, command execution, formatting)

### Architectural Boundaries

- **Presentation Layer** (UI, ViewModels, Converters): Handles user interaction and display
- **Service Layer** (Services): Implements application features
- **Core Layer** (Core, Models): PowerShell integration and data representation

## Usage

1. **Launch** the application
2. **Browse Modules**: Discover available PowerShell modules from the module list
3. **Select Cmdlet**: Choose a cmdlet to see its parameters and help text
4. **Fill Parameters**: Provide parameter values through the form UI
5. **Preview**: Review the generated command before execution
6. **Execute**: Run the command and view results in the output panel
7. **Manage Output**: Switch between grid and text output formats as needed

## Contributing

When contributing to PSForge, please:

- Follow the existing code structure and naming conventions
- Maintain separation between business logic and UI code
- Add appropriate error handling and logging
- Update documentation for significant changes
- Test changes thoroughly before submitting

## Troubleshooting

### Application fails to start
- Verify .NET 8.0 runtime is installed
- Check Windows is supported (Windows 7 SP1 or higher)
- Run from Visual Studio for detailed error messages

### PowerShell commands fail to execute
- Ensure PowerShell 5.0+ is installed
- Check that cmdlet names and parameters are valid
- Verify you have appropriate permissions for the command

### UI controls not displaying correctly
- Clear `bin/` and `obj/` directories
- Rebuild the entire solution
- Check Visual Studio UI toolkit packages are correctly installed

## License

[Add your license information here]

## Support

For issues, questions, or suggestions, please [create an issue](../../issues) in the repository.
