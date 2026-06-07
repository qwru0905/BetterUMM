# BetterUMM WPF → Avalonia UI Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace BetterUMM's WPF UI (Windows-only) with Avalonia UI so the app runs on Windows, Linux, and macOS, while preserving all existing behavior and the MVVM structure.

**Architecture:** `MainViewModel`/`RelayCommand`/services are framework-agnostic BCL code and stay as-is except for two WPF-only touch points (`System.Windows.MessageBox`, `Microsoft.Win32.OpenFileDialog`, and `CommandManager.RequerySuggested`). The UI shell (`App`, `MainWindow`, XAML, converters) is rewritten for Avalonia. The `csproj` switches from `net8.0-windows`+`UseWPF` to plain `net8.0` with Avalonia package references.

**Tech Stack:** .NET 8, Avalonia 12.0.4 (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Controls.DataGrid` 12.0.0), `MessageBox.Avalonia` 12.0.0 (namespace `MsBox.Avalonia`), existing `Mono.Cecil`/`Newtonsoft.Json`.

---

## Important notes before starting

- **No non-Windows test environment is available.** "Tests" in this plan mean: the project builds (`dotnet build`), and a manual smoke test of the running app on Windows. Cross-platform runtime behavior is verified later (in a separate plan) via cross-compilation only.
- **This is a framework swap, not an incremental feature.** WPF and Avalonia cannot coexist in one project — the build will be intentionally broken from Task 3 through Task 7, and restored to green at the end of Task 7. This is called out explicitly so you don't stop and "fix" it mid-way.
- All work happens on the `feature/cross-platform-port` branch (already checked out).

## File Structure Overview

| Action | Path | Responsibility |
|---|---|---|
| Modify | `BetterUMM/ViewModels/RelayCommand.cs` | Replace WPF `CommandManager.RequerySuggested` with manual `CanExecuteChanged` raising |
| Modify | `BetterUMM/ViewModels/MainViewModel.cs` | Wire manual command re-evaluation; later, swap dialogs/pickers to Avalonia equivalents |
| Modify | `BetterUMM/BetterUMM.csproj` | TFM `net8.0-windows`→`net8.0`, drop `UseWPF`, add Avalonia packages |
| Delete | `BetterUMM/AssemblyInfo.cs` | WPF-only `ThemeInfo` assembly attribute, meaningless without WPF |
| Delete | `BetterUMM/App.xaml`, `BetterUMM/App.xaml.cs` | Replaced by Avalonia equivalents |
| Create | `BetterUMM/Program.cs` | Avalonia entry point / `AppBuilder` bootstrap |
| Create | `BetterUMM/App.axaml`, `BetterUMM/App.axaml.cs` | Avalonia application class, theme, global exception hooks |
| Delete | `BetterUMM/MainWindow.xaml`, `BetterUMM/MainWindow.xaml.cs` | Replaced by Avalonia equivalents |
| Create | `BetterUMM/MainWindow.axaml`, `BetterUMM/MainWindow.axaml.cs` | Main window UI, ported to Avalonia XAML |
| Modify | `BetterUMM/Converters/ValueConverters.cs` | Port `InverseBooleanConverter` to `Avalonia.Data.Converters.IValueConverter`; delete now-unused `BooleanToVisibilityConverter` |
| Modify | `BetterUMM/Services/LoggerService.cs` | Remove unused `using System.Windows;` |

---

## Task 1: Make `RelayCommand` re-evaluate `CanExecute` without WPF

WPF's `RelayCommand.CanExecuteChanged` delegates to `CommandManager.RequerySuggested`, which is a WPF-only mechanism that automatically re-runs `CanExecute` after UI events. Avalonia has no equivalent — commands must raise `CanExecuteChanged` explicitly. This task is framework-agnostic (compiles fine under the current WPF project) so it's a safe standalone first step.

**Files:**
- Modify: `BetterUMM/ViewModels/RelayCommand.cs`
- Modify: `BetterUMM/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Replace `CommandManager.RequerySuggested` with a manual event**

Edit `BetterUMM/ViewModels/RelayCommand.cs` — replace the `CanExecuteChanged` event implementation:

```csharp
using System;
using System.Windows.Input;

namespace BetterUMM.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);

        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged;

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
```

(`ICommand` lives in the BCL `System.Windows.Input` namespace, not WPF — the `using` stays.)

- [ ] **Step 2: Wire `MainViewModel` to call `RaiseCanExecuteChanged` when `HasUnsavedChanges` changes**

In `BetterUMM/ViewModels/MainViewModel.cs`:

1. Add a field and change `SaveModStatesCommand` to be backed by a concrete `RelayCommand`:

Replace:
```csharp
        public ICommand SaveModStatesCommand { get; }
```
with:
```csharp
        public ICommand SaveModStatesCommand => _saveModStatesCommand;
```

Add a field near the other `private readonly` service fields:
```csharp
        private readonly RelayCommand _saveModStatesCommand;
```

2. In the constructor, replace:
```csharp
            SaveModStatesCommand = new RelayCommand(_ => SaveModStates(), _ => HasUnsavedChanges);
```
with:
```csharp
            _saveModStatesCommand = new RelayCommand(_ => SaveModStates(), _ => HasUnsavedChanges);
```

3. Add a helper method (place it near `OnModPropertyChanged`):
```csharp
        private void NotifyHasUnsavedChangesChanged()
        {
            OnPropertyChanged(nameof(HasUnsavedChanges));
            _saveModStatesCommand.RaiseCanExecuteChanged();
        }
```

4. Replace the three existing `OnPropertyChanged(nameof(HasUnsavedChanges));` call sites with `NotifyHasUnsavedChangesChanged();`:
   - End of `LoadMods()`
   - In `OnModPropertyChanged` (the `if (e.PropertyName == nameof(ModInfo.IsDirty))` block — this also removes the now-stale comment `// CommandManager.RequerySuggested가 자동으로 CanExecute 재평가함`)
   - In `SaveModStates()` after `mod.MarkAsClean()`

The `OnModPropertyChanged` body becomes:
```csharp
        private void OnModPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ModInfo.IsDirty))
                NotifyHasUnsavedChangesChanged();
        }
```

- [ ] **Step 3: Build and smoke-test**

Run: `dotnet build BetterUMM/BetterUMM.csproj`
Expected: Build succeeds (this is still the WPF project at this point).

Run the app (`dotnet run --project BetterUMM/BetterUMM.csproj`), select a game with mods, toggle a mod's enabled checkbox, and confirm the "Save" button enables; click Save and confirm it disables again. This exercises the new manual `CanExecuteChanged` path.

- [ ] **Step 4: Commit**

```bash
git add BetterUMM/ViewModels/RelayCommand.cs BetterUMM/ViewModels/MainViewModel.cs
git commit -m "refactor/RelayCommand가 CanExecuteChanged를 직접 통지하도록 변경 (Avalonia 호환 준비)"
```

---

## Task 2: Switch the project to Avalonia

This breaks the build intentionally — WPF types (`System.Windows.*`) disappear once `UseWPF` is removed and the TFM drops `-windows`. The build stays red through Task 7.

**Files:**
- Modify: `BetterUMM/BetterUMM.csproj`

- [ ] **Step 1: Replace the `csproj` contents**

Replace the entire contents of `BetterUMM/BetterUMM.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="12.0.4" />
    <PackageReference Include="Avalonia.Desktop" Version="12.0.4" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="12.0.4" />
    <PackageReference Include="Avalonia.Controls.DataGrid" Version="12.0.0" />
    <PackageReference Include="MessageBox.Avalonia" Version="12.0.0" />
    <PackageReference Include="Mono.Cecil" Version="0.11.6" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
  </ItemGroup>

  <ItemGroup>
    <None Include="Resources\UnityModManager\**">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <Link>UnityModManager\%(RecursiveDir)%(FileName)%(Extension)</Link>
    </None>
    <None Update="Resources\UnityModManagerConfig.xml">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <Link>UnityModManagerConfig.xml</Link>
    </None>
    <None Update="Resources\winhttp_x64.dll">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <Link>winhttp_x64.dll</Link>
    </None>
    <None Update="Resources\winhttp_x86.dll">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <Link>winhttp_x86.dll</Link>
    </None>
  </ItemGroup>

</Project>
```

Note: the package id is `MessageBox.Avalonia` but its C# namespace is `MsBox.Avalonia` (the project renamed its namespace while keeping the older package id for v12). This is intentional, not a typo — Tasks 4 and 6 use `using MsBox.Avalonia;`.

- [ ] **Step 2: Restore packages**

Run: `dotnet restore BetterUMM/BetterUMM.csproj`
Expected: Restore succeeds and downloads the Avalonia/MessageBox.Avalonia packages. (Build will still fail — that's expected, proceed to Task 3.)

---

## Task 3: Avalonia application bootstrap (`Program.cs`, `App`)

**Files:**
- Create: `BetterUMM/Program.cs`
- Create: `BetterUMM/App.axaml`
- Create: `BetterUMM/App.axaml.cs`
- Delete: `BetterUMM/App.xaml`
- Delete: `BetterUMM/App.xaml.cs`
- Delete: `BetterUMM/AssemblyInfo.cs`

- [ ] **Step 1: Delete the old WPF entry-point files**

```bash
git rm BetterUMM/App.xaml BetterUMM/App.xaml.cs BetterUMM/AssemblyInfo.cs
```

(`AssemblyInfo.cs` only contains the WPF-only `[assembly: ThemeInfo(...)]` attribute, which has no meaning without `UseWPF`.)

- [ ] **Step 2: Create `Program.cs`**

Create `BetterUMM/Program.cs`:

```csharp
using Avalonia;
using System;

namespace BetterUMM
{
    internal class Program
    {
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
```

- [ ] **Step 3: Create `App.axaml`**

Create `BetterUMM/App.axaml`:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="BetterUMM.App"
             xmlns:conv="clr-namespace:BetterUMM.Converters"
             RequestedThemeVariant="Default">
    <Application.Styles>
        <FluentTheme />
    </Application.Styles>

    <Application.Resources>
        <conv:InverseBooleanConverter x:Key="InverseBooleanConverter"/>
    </Application.Resources>
</Application>
```

- [ ] **Step 4: Create `App.axaml.cs`**

Create `BetterUMM/App.axaml.cs`. This replaces WPF's `OnStartup`/`DispatcherUnhandledException`/`CurrentDomain_UnhandledException` hooks. Avalonia has no `DispatcherUnhandledException` equivalent, so UI-thread exceptions are caught the same way as background-thread ones — via `AppDomain.CurrentDomain.UnhandledException` (this can't prevent the crash the way WPF's handler could, but it still logs and shows the dialog before the process exits, matching the original intent):

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using BetterUMM.Services;
using BetterUMM.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using System;
using System.Threading.Tasks;

namespace BetterUMM
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var window = new MainWindow();
                window.DataContext = new MainViewModel(window);
                desktop.MainWindow = window;
            }

            LoggerService.Log("Application started.");

            base.OnFrameworkInitializationCompleted();
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LoggerService.LogException(ex, "CurrentDomain_UnhandledException");
                ShowExceptionMessageBox(ex);
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LoggerService.LogException(e.Exception, "TaskScheduler_UnobservedTaskException");
            e.SetObserved();
        }

        private void ShowExceptionMessageBox(Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var box = MessageBoxManager.GetMessageBoxStandard(
                    "오류",
                    $"예기치 않은 오류가 발생했습니다. 로그 파일을 확인해주세요.\n\n오류 메시지: {ex.Message}",
                    ButtonEnum.Ok,
                    Icon.Error);
                _ = box.ShowAsync();
            });
        }
    }
}
```

- [ ] **Step 4: Commit is deferred** — the project doesn't compile yet (no `MainWindow`, converters not ported). Continue to Task 4.

---

## Task 4: Port the converters

The original `BooleanToVisibilityConverter` converts `bool` → WPF's `Visibility` enum. Avalonia controls use a plain `bool IsVisible` property instead, so once XAML binds directly to `IsVisible`, that converter becomes redundant — `InverseBooleanConverter` alone covers every "Inverse" usage in `MainWindow.xaml`. Delete `BooleanToVisibilityConverter` rather than porting dead code.

**Files:**
- Modify: `BetterUMM/Converters/ValueConverters.cs`

- [ ] **Step 1: Replace the file contents**

Replace the entire contents of `BetterUMM/Converters/ValueConverters.cs`:

```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BetterUMM.Converters
{
    public class InverseBooleanConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return value;
        }
    }
}
```

- [ ] **Step 2: Commit is deferred** — continue to Task 5 (the project still won't compile until `MainWindow` exists).

---

## Task 5: Port `MainWindow` to Avalonia XAML

WPF's `DataTrigger`/`Style.Triggers` have no Avalonia equivalent — Avalonia uses style **selectors** with dynamic style classes (`Classes.xxx="{Binding ...}"` + `<Style Selector="Control.xxx">`). Also: WPF's `GroupBox` doesn't exist in Avalonia (replaced with a `Border`+`DockPanel` header), `Visibility` becomes a plain `bool IsVisible`, and `ToolTip` becomes the `ToolTip.Tip` attached property.

**Files:**
- Create: `BetterUMM/MainWindow.axaml`
- Create: `BetterUMM/MainWindow.axaml.cs`
- Delete: `BetterUMM/MainWindow.xaml`
- Delete: `BetterUMM/MainWindow.xaml.cs`

- [ ] **Step 1: Delete the old WPF window files**

```bash
git rm BetterUMM/MainWindow.xaml BetterUMM/MainWindow.xaml.cs
```

- [ ] **Step 2: Create `MainWindow.axaml`**

Create `BetterUMM/MainWindow.axaml`:

```xml
<Window x:Class="BetterUMM.MainWindow"
        xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        Title="Better Unity Mod Manager" Height="600" Width="900" Background="#F0F0F0">
    <Window.Styles>
        <Style Selector="TextBlock.installed">
            <Setter Property="Foreground" Value="Green"/>
        </Style>
        <Style Selector="Button.unsaved">
            <Setter Property="FontWeight" Value="Bold"/>
        </Style>
    </Window.Styles>
    <Grid Margin="10">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Header: Game Selection -->
        <StackPanel Grid.Row="0" Margin="0,0,0,10">
            <StackPanel Orientation="Horizontal" Margin="0,0,0,5">
                <TextBlock Text="Select Game:" VerticalAlignment="Center" Margin="0,0,10,0" FontWeight="Bold"/>
                <Button Content="Browse..." Command="{Binding SelectGameCommand}" Margin="0,0,10,0" Padding="10,2"/>
                <TextBlock Text="{Binding SelectedGame.Name, TargetNullValue='No game selected'}" VerticalAlignment="Center" FontWeight="Bold" Foreground="Blue"/>
            </StackPanel>
            <Border BorderBrush="Gray" BorderThickness="1" Padding="5" Background="#E0E0E0">
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="Path: " FontWeight="Bold"/>
                    <TextBlock Text="{Binding SelectedGame.Path, TargetNullValue='N/A'}"/>
                    <TextBlock Text=" | UMM: " FontWeight="Bold" Margin="10,0,0,0"/>
                    <TextBlock Text="{Binding PatchStatusText}" Foreground="Gray" Classes.installed="{Binding IsUmmInstalled}"/>
                </StackPanel>
            </Border>
            <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
                <Button Content="{Binding PatchButtonText}" Command="{Binding PatchCommand}" Padding="10,2" VerticalAlignment="Center"/>
                <TextBlock Text="Patch Method:" VerticalAlignment="Center" Margin="20,0,10,0" FontWeight="Bold"
                           IsVisible="{Binding IsUmmInstalled, Converter={StaticResource InverseBooleanConverter}}"/>
                <ComboBox ItemsSource="{Binding AvailablePatchMethods}"
                          SelectedItem="{Binding TargetPatchMethod}"
                          Width="100" VerticalAlignment="Center"
                          IsEnabled="{Binding IsUmmInstalled, Converter={StaticResource InverseBooleanConverter}}"
                          IsVisible="{Binding IsUmmInstalled, Converter={StaticResource InverseBooleanConverter}}"/>
            </StackPanel>
        </StackPanel>

        <!-- Main Content: Mod List -->
        <Border Grid.Row="1" BorderBrush="Gray" BorderThickness="1" CornerRadius="4">
            <DockPanel>
                <TextBlock Text="Installed Mods" FontWeight="Bold" Margin="8,6" DockPanel.Dock="Top"/>
                <DataGrid ItemsSource="{Binding Mods}" AutoGenerateColumns="False" SelectionMode="Single">
                    <DataGrid.Columns>
                        <DataGridCheckBoxColumn Header="Enabled" Binding="{Binding IsEnabled, UpdateSourceTrigger=PropertyChanged}" Width="60"/>
                        <DataGridTextColumn Header="Name" Binding="{Binding DisplayName}" Width="*" IsReadOnly="True"/>
                        <DataGridTextColumn Header="Version" Binding="{Binding Version}" Width="100" IsReadOnly="True"/>
                        <DataGridTextColumn Header="Author" Binding="{Binding Author}" Width="150" IsReadOnly="True"/>
                        <!-- 변경 여부 표시 -->
                        <DataGridTemplateColumn Header="" Width="20">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate>
                                    <Ellipse Width="8" Height="8" Fill="Orange" ToolTip.Tip="저장되지 않은 변경사항" IsVisible="{Binding IsDirty}"/>
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                    </DataGrid.Columns>
                </DataGrid>
            </DockPanel>
        </Border>

        <!-- Mod Action Buttons -->
        <StackPanel Orientation="Horizontal" Grid.Row="2" Margin="0,5,0,0">
            <Button Content="Install Mod" Command="{Binding InstallModCommand}" Padding="10,2" Margin="0,0,5,0"/>
            <Button Content="Refresh" Command="{Binding RefreshCommand}" Padding="10,2" Margin="0,0,5,0"/>
            <Button Content="Save" Command="{Binding SaveModStatesCommand}" Padding="10,2" Classes.unsaved="{Binding HasUnsavedChanges}"/>
        </StackPanel>

        <!-- Footer: Profile Management & Settings -->
        <DockPanel Grid.Row="3" Margin="0,10,0,0">
            <StackPanel Orientation="Horizontal" DockPanel.Dock="Left">
                <TextBlock Text="Profiles:" VerticalAlignment="Center" Margin="0,0,10,0" FontWeight="Bold"/>
                <ComboBox Width="150" Margin="0,0,10,0"/>
                <Button Content="Switch Profile" Margin="0,0,5,0" Padding="5,2"/>
                <Button Content="New Profile" Padding="5,2"/>
            </StackPanel>

            <StackPanel Orientation="Horizontal" DockPanel.Dock="Right" HorizontalAlignment="Right">
                <TextBlock Text="Log Level:" VerticalAlignment="Center" Margin="0,0,10,0" FontWeight="Bold"/>
                <ComboBox ItemsSource="{Binding AvailableLogLevels}"
                          SelectedItem="{Binding SelectedLogLevel}"
                          Width="100" VerticalAlignment="Center"/>
            </StackPanel>
        </DockPanel>
    </Grid>
</Window>
```

Note: `AvailableLogLevels`/`SelectedLogLevel` and the Profiles combo box are bound to properties that don't exist on `MainViewModel` today — that's a pre-existing gap in the WPF version too (the bindings silently no-op). This plan preserves that behavior as-is; wiring them up is a separate feature, not part of this port.

- [ ] **Step 3: Create `MainWindow.axaml.cs`**

Create `BetterUMM/MainWindow.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace BetterUMM
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
    }
}
```

- [ ] **Step 4: Commit is deferred** — `MainViewModel` still references WPF types (`System.Windows.MessageBox`, `Microsoft.Win32.OpenFileDialog`). Continue to Task 6.

---

## Task 6: Port `MainViewModel` dialogs and file pickers to Avalonia

WPF's `OpenFileDialog.ShowDialog()` and `MessageBox.Show(...)` are synchronous, no-window-reference APIs. Avalonia's equivalents are asynchronous and require a `Window`/`TopLevel` to host them — `Window` derives from `TopLevel` and exposes `StorageProvider` directly. This requires `MainViewModel` to receive a `Window` reference (already wired in `App.axaml.cs` from Task 3: `new MainViewModel(window)`), and the affected methods become `async Task`.

**Files:**
- Modify: `BetterUMM/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Replace the entire file contents**

Replace the entire contents of `BetterUMM/ViewModels/MainViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using BetterUMM.Models;
using BetterUMM.Services;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace BetterUMM.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ConfigService _configService = new();
        private readonly ModService _modService = new();
        private readonly PatchService _patchService = new();
        private readonly ProfileService _profileService = new();
        private readonly Window _window;
        private readonly RelayCommand _saveModStatesCommand;

        private GameInfo? _selectedGame;
        public GameInfo? SelectedGame
        {
            get => _selectedGame;
            set
            {
                if (_selectedGame != value)
                {
                    _selectedGame = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TargetPatchMethod));
                    RefreshPatchStatus();
                    LoadMods();
                }
            }
        }

        private PatchStatus _patchStatus = PatchStatus.NotInstalled;
        public PatchStatus PatchStatus
        {
            get => _patchStatus;
            private set
            {
                _patchStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PatchStatusText));
                OnPropertyChanged(nameof(IsUmmInstalled));
                OnPropertyChanged(nameof(PatchButtonText));
            }
        }

        public string PatchStatusText => PatchStatus switch
        {
            PatchStatus.Doorstop          => "설치됨 (Doorstop)",
            PatchStatus.AssemblyInjection => "설치됨 (Assembly)",
            _                             => "미설치"
        };

        public bool IsUmmInstalled => PatchStatus != PatchStatus.NotInstalled;
        public string PatchButtonText => IsUmmInstalled ? "Uninstall UMM" : "Install UMM";

        // 변경된 모드가 있는지 여부 (저장 버튼 활성화 조건)
        public bool HasUnsavedChanges => Mods.Any(m => m.IsDirty);

        public ObservableCollection<GameInfo> Games { get; } = new();
        public ObservableCollection<ModInfo> Mods { get; } = new();

        public IEnumerable<PatchMethod> AvailablePatchMethods =>
            Enum.GetValues(typeof(PatchMethod)).Cast<PatchMethod>().Where(m => m != PatchMethod.None);

        public PatchMethod TargetPatchMethod
        {
            get => SelectedGame?.CurrentPatchMethod ?? PatchMethod.Doorstop;
            set
            {
                if (SelectedGame != null && SelectedGame.CurrentPatchMethod != value)
                {
                    SelectedGame.CurrentPatchMethod = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand PatchCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SelectGameCommand { get; }
        public ICommand SaveModStatesCommand => _saveModStatesCommand;
        public ICommand InstallModCommand { get; }

        public MainViewModel(Window window)
        {
            _window = window;

            _configService.LoadConfig();
            foreach (var config in _configService.GetAllConfigs())
                Games.Add(new GameInfo { Name = config.Name, Folder = config.Folder });

            PatchCommand          = new RelayCommand(async _ => await PatchSelectedGameAsync());
            RefreshCommand        = new RelayCommand(_ => LoadMods());
            SelectGameCommand     = new RelayCommand(async _ => await SelectGameAsync());
            _saveModStatesCommand = new RelayCommand(async _ => await SaveModStatesAsync(), _ => HasUnsavedChanges);
            InstallModCommand     = new RelayCommand(async _ => await InstallModAsync());
        }

        private async Task SelectGameAsync()
        {
            var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Game Executable",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Executable files") { Patterns = new[] { "*.exe" } },
                    FilePickerFileTypes.All
                }
            });

            if (files.Count == 0) return;

            string path = files[0].Path.LocalPath;
            string folderName = Path.GetFileName(Path.GetDirectoryName(path)) ?? "";
            string exeName = Path.GetFileName(path);

            // 1차: 폴더명 매칭, 실패 시 2차: exe 이름으로 매칭
            var config = _configService.GetGameConfig(folderName)
                      ?? _configService.GetGameConfigByExe(exeName);

            SelectedGame = new GameInfo
            {
                Name = config?.Name ?? Path.GetFileNameWithoutExtension(path),
                Path = path,
                GameDataPath = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}_Data"),
                AssemblyName = config != null
                    ? (config.EntryPoint.Split('[', ']').Length > 2 ? config.EntryPoint.Split('[', ']')[1] : "Assembly-CSharp.dll")
                    : "Assembly-CSharp.dll",
                PatchTarget = config?.EntryPoint ?? string.Empty,
                CurrentPatchMethod = PatchMethod.Doorstop,

                Folder            = config?.Folder ?? folderName,
                ModsDirectory     = config?.ModsDirectory ?? "Mods",
                ModInfo           = config?.ModInfo ?? "Info.json",
                GameExe           = config?.GameExe ?? Path.GetFileName(path),
                EntryPoint        = config?.EntryPoint ?? string.Empty,
                StartingPoint     = config?.StartingPoint ?? string.Empty,
                UIStartingPoint   = config?.UIStartingPoint ?? string.Empty,
                OldPatchTarget    = config?.OldPatchTarget ?? string.Empty,
                GameVersionPoint  = config?.GameVersionPoint ?? string.Empty,
                MinimalManagerVersion = config?.MinimalManagerVersion ?? string.Empty,
                HarmonyVersion    = config?.HarmonyVersion ?? string.Empty
            };
        }

        private void RefreshPatchStatus()
        {
            if (SelectedGame == null || string.IsNullOrEmpty(SelectedGame.Path))
            {
                PatchStatus = PatchStatus.NotInstalled;
                return;
            }

            try
            {
                PatchStatus = _patchService.GetPatchStatus(SelectedGame);
                if (PatchStatus == PatchStatus.Doorstop)
                    SelectedGame.CurrentPatchMethod = PatchMethod.Doorstop;
                else if (PatchStatus == PatchStatus.AssemblyInjection)
                    SelectedGame.CurrentPatchMethod = PatchMethod.Assembly;

                OnPropertyChanged(nameof(TargetPatchMethod));
            }
            catch
            {
                PatchStatus = PatchStatus.NotInstalled;
            }
        }

        private void LoadMods()
        {
            if (SelectedGame == null || string.IsNullOrEmpty(SelectedGame.Path)) return;

            Mods.Clear();

            string gameDir = Path.GetDirectoryName(SelectedGame.Path)!;
            string modsPath = Path.Combine(gameDir, SelectedGame.ModsDirectory);
            string paramsPath = ModService.GetParamsPath(SelectedGame.GameDataPath);

            var mods = _modService.ScanMods(modsPath, paramsPath);
            foreach (var mod in mods)
            {
                mod.MarkAsClean(); // 로드 시점을 기준으로 dirty 추적 시작
                mod.PropertyChanged += OnModPropertyChanged;
                Mods.Add(mod);
            }

            NotifyHasUnsavedChangesChanged();
        }

        private void OnModPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ModInfo.IsDirty))
                NotifyHasUnsavedChangesChanged();
        }

        private void NotifyHasUnsavedChangesChanged()
        {
            OnPropertyChanged(nameof(HasUnsavedChanges));
            _saveModStatesCommand.RaiseCanExecuteChanged();
        }

        private async Task SaveModStatesAsync()
        {
            var dirtyMods = Mods.Where(m => m.IsDirty).ToList();
            if (!dirtyMods.Any()) return;

            try
            {
                string paramsPath = ModService.GetParamsPath(SelectedGame!.GameDataPath);
                _modService.SaveAllEnabledStates(dirtyMods, paramsPath);
                foreach (var mod in dirtyMods)
                    mod.MarkAsClean();

                NotifyHasUnsavedChangesChanged();
                await ShowMessageAsync($"{dirtyMods.Count}개 모드 상태가 저장되었습니다.", "저장 완료", Icon.Info);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync($"저장 실패: {ex.Message}", "오류", Icon.Error);
            }
        }

        private async Task InstallModAsync()
        {
            if (SelectedGame == null || string.IsNullOrEmpty(SelectedGame.Path))
            {
                await ShowMessageAsync("게임을 먼저 선택하세요.");
                return;
            }

            var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "모드 zip 파일 선택",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Zip files") { Patterns = new[] { "*.zip" } } }
            });

            if (files.Count == 0) return;

            string gameDir = Path.GetDirectoryName(SelectedGame.Path)!;
            string modsPath = Path.Combine(gameDir, SelectedGame.ModsDirectory);

            try
            {
                _modService.InstallMod(files[0].Path.LocalPath, modsPath);
                LoadMods(); // 설치 후 목록 갱신
                await ShowMessageAsync("모드가 설치되었습니다.", "설치 완료", Icon.Info);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync($"설치 실패: {ex.Message}", "오류", Icon.Error);
            }
        }

        private async Task PatchSelectedGameAsync()
        {
            if (SelectedGame == null || string.IsNullOrEmpty(SelectedGame.Path)) return;

            bool ok;
            if (IsUmmInstalled)
            {
                ok = PatchStatus == PatchStatus.Doorstop
                    ? _patchService.RemoveDoorstop(SelectedGame)
                    : _patchService.RemoveAssembly(SelectedGame);

                if (ok) RefreshPatchStatus();
                await ShowMessageAsync(ok ? "제거 성공!" : "제거 실패. 로그를 확인하세요.");
                return;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string ummSourceDir = Path.Combine(baseDir, "UnityModManager");
            if (!Directory.Exists(ummSourceDir))
            {
                await ShowMessageAsync($"UnityModManager 리소스 폴더를 찾을 수 없습니다: {ummSourceDir}");
                return;
            }

            string[] libs = Directory.GetFiles(ummSourceDir, "*", SearchOption.AllDirectories);
            if (libs.Length == 0)
            {
                await ShowMessageAsync("UnityModManager 라이브러리 파일을 찾을 수 없습니다.");
                return;
            }

            if (SelectedGame.CurrentPatchMethod == PatchMethod.Doorstop)
            {
                ok = _patchService.InstallDoorstop(
                    SelectedGame,
                    Path.Combine(baseDir, "winhttp_x64.dll"),
                    Path.Combine(baseDir, "winhttp_x86.dll"),
                    libs);
            }
            else
            {
                ok = _patchService.InstallAssembly(SelectedGame, libs);
            }

            if (ok) RefreshPatchStatus();
            await ShowMessageAsync(ok ? "패치 성공!" : "패치 실패. 로그를 확인하세요.");
        }

        private static async Task ShowMessageAsync(string message, string title = "", Icon icon = Icon.None)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok, icon);
            await box.ShowAsync();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
```

Key changes from the WPF version:
- Constructor now takes a `Window` (passed from `App.axaml.cs`) so it can reach `_window.StorageProvider` for file pickers (`Window` derives from `TopLevel`, which exposes `StorageProvider` directly — no `TopLevel.GetTopLevel` ceremony needed)
- `SelectGame`/`InstallMod`/`SaveModStates`/`PatchSelectedGame` become `async Task` methods (`...Async`), using `storageProvider.OpenFilePickerAsync` instead of `Microsoft.Win32.OpenFileDialog.ShowDialog()`
- All `System.Windows.MessageBox.Show(...)` calls funnel through a new `ShowMessageAsync` helper wrapping `MessageBoxManager.GetMessageBoxStandard(...).ShowAsync()` (DRY — replaces 9 near-identical call sites)
- `RelayCommand` registrations use `async _ => await ...Async()` lambdas (an async lambda assigned to `Action<object?>` becomes `async void`, which is the standard `ICommand.Execute` pattern since the interface method returns `void`)

- [ ] **Step 2: Commit is deferred** — continue to Task 7 to finish restoring the build.

---

## Task 7: Clean up `LoggerService` and restore a green build

**Files:**
- Modify: `BetterUMM/Services/LoggerService.cs`

- [ ] **Step 1: Remove the unused WPF import**

In `BetterUMM/Services/LoggerService.cs`, delete the line:
```csharp
using System.Windows;
```
(Confirmed unused — `grep -n "System.Windows\|Application\.\|Dispatcher\|MessageBox" Services/LoggerService.cs` only matches the `using` line itself.)

- [ ] **Step 2: Build**

Run: `dotnet build BetterUMM/BetterUMM.csproj`
Expected: Build succeeds with 0 errors. If there are XAML binding/compile errors, they'll reference `MainWindow.axaml` or `App.axaml` line numbers — fix by re-checking the XAML against Task 5/Step 2 (common issues: missing `xmlns`, mistyped `Classes.xxx` selector names, or `StaticResource` keys that don't match `App.axaml` resource keys).

- [ ] **Step 3: Commit the whole migration**

```bash
git add -A
git commit -m "feat/WPF UI를 Avalonia로 전면 교체 (Mac·Linux 포팅 1단계)"
```

---

## Task 8: Manual smoke test on Windows

This is the only "test" available for UI behavior — there's no automated UI test harness in this project, and Linux/macOS runtime behavior is out of scope for this plan (verified later via cross-compilation in a separate plan).

**Files:** none (manual verification only)

- [ ] **Step 1: Run the app**

Run: `dotnet run --project BetterUMM/BetterUMM.csproj`
Expected: A window titled "Better Unity Mod Manager" opens at 900x600, matching the original WPF layout (header game-selection bar, mod DataGrid, action buttons, footer).

- [ ] **Step 2: Walk through the golden path**

Verify each of these (comparing against the pre-migration behavior you observed in Task 1/Step 3):
- "Browse..." opens a native file picker; selecting a game `.exe` populates the Name/Path fields and the UMM status line
- The mod `DataGrid` lists mods with Enabled checkbox, Name, Version, Author columns, and an orange dot appears on rows with unsaved changes
- Toggling a mod's Enabled checkbox turns the "Save" button bold (via the `Button.unsaved` style class) and enables it; clicking "Save" shows a "저장 완료" dialog, the dot disappears, and the button returns to normal weight and disabled
- "Install Mod" opens a `.zip` file picker and, on success, shows "모드가 설치되었습니다." and refreshes the list
- "Install UMM"/"Uninstall UMM" button triggers patch/unpatch and shows the corresponding success/failure dialog; the UMM status line switches between gray "미설치" and green "설치됨 (...)" (the `TextBlock.installed` style class)
- The "Patch Method" combo box is hidden while UMM is installed and visible otherwise (via `InverseBooleanConverter` + `IsVisible`)

- [ ] **Step 3: Check the log file for unexpected errors**

Open the most recent `logs/log_<date>.txt` file next to the built executable and confirm there are no new exceptions beyond what existed before the migration.

- [ ] **Step 4: Report results**

If every item in Step 2 behaves like the original WPF app, the migration is complete. If something differs, note exactly which behavior differs and on which step — that becomes the basis for a follow-up fix commit (don't fold speculative fixes into this task).

---

## What's intentionally NOT in this plan

These were identified during design but belong to later phases (separate plans, per the design doc `docs/superpowers/specs/2026-06-07-cross-platform-port-design.md`):

- `PatchService` Doorstop platform branching (Linux `libdoorstop.so`/`run.sh`, macOS `libdoorstop.dylib`)
- ELF header parsing for Linux `libdoorstop.so` x64/x86 selection
- `GameInfo`/game-detection platform branching (file picker filters, `.app` bundle handling, `GameDataPath` derivation on macOS)
- `linux-x64`/`osx-x64`/`osx-arm64` publish profiles and Doorstop resource bundling
