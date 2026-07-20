# Mod Delete (Uninstall) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user delete an installed mod from the mod list UI, using the existing (currently unwired) `ModService.UninstallMod` method.

**Architecture:** Add a per-row delete button to the mod `ListBox` in `MainWindow.axaml`, bound to a new `DeleteModCommand` on `MainViewModel`. The command shows a Yes/No confirmation dialog, then calls `ModService.UninstallMod` (already implemented, deletes the mod's folder) and reloads the mod list.

**Tech Stack:** C# / .NET 8, Avalonia UI, MsBox.Avalonia (message boxes), xUnit (tests).

## Global Constraints

- Confirmation dialog required before deletion (irreversible folder delete) — spec decision, see `docs/superpowers/specs/2026-07-07-mod-delete-design.md`.
- No cleanup of the orphaned `<Mod Id="...">` entry in `Params.xml` after delete — out of scope per spec.
- No multi-select delete, no undo — out of scope per spec.
- Follow existing code style: this codebase's newer UI-facing strings are in English (see `08a04d6`); write all new user-facing strings in English. Existing Korean strings elsewhere in `MainViewModel.cs` are pre-existing and out of scope — do not touch them.

---

### Task 1: `ModService.UninstallMod` test coverage

`UninstallMod` (`BetterUMM/Services/ModService.cs:137-141`) already exists but has zero test coverage today, and is about to become reachable from the UI. Lock down its contract before wiring it up.

**Files:**
- Create: `BetterUMM.Tests/Services/ModServiceTests.cs`

**Interfaces:**
- Consumes: `BetterUMM.Services.ModService.UninstallMod(ModInfo mod)` (existing, void, deletes `mod.FolderPath` recursively if it exists), `BetterUMM.Models.ModInfo` (existing, has `FolderPath` settable property).
- Produces: nothing new — this task only adds tests for existing code.

- [ ] **Step 1: Write the test file**

```csharp
using System;
using System.IO;
using BetterUMM.Models;
using BetterUMM.Services;
using Xunit;

namespace BetterUMM.Tests.Services
{
    public class ModServiceTests
    {
        [Fact]
        public void UninstallMod_ForExistingFolder_DeletesFolderAndContents()
        {
            string dir = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(dir, "Info.json"), "{}");
                File.WriteAllText(Path.Combine(dir, "mod.dll"), "fake-dll-bytes");
                var mod = new ModInfo { Id = "test.mod", FolderPath = dir };

                new ModService().UninstallMod(mod);

                Assert.False(Directory.Exists(dir));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void UninstallMod_ForMissingFolder_DoesNotThrow()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"betterumm-missing-{Guid.NewGuid():N}");
            var mod = new ModInfo { Id = "test.mod", FolderPath = dir };

            var exception = Record.Exception(() => new ModService().UninstallMod(mod));

            Assert.Null(exception);
        }

        private static string CreateTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"betterumm-modservice-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they pass**

Run: `dotnet test BetterUMM.Tests/BetterUMM.Tests.csproj --filter ModServiceTests`
Expected: both tests PASS (the implementation already exists — this confirms its contract, it should not need changes).

- [ ] **Step 3: Commit**

```bash
git add BetterUMM.Tests/Services/ModServiceTests.cs
git commit -m "test: add coverage for ModService.UninstallMod before wiring it into the UI"
```

---

### Task 2: `DeleteModCommand` on `MainViewModel`

Wire the mod deletion flow into the view model: confirm, delete, refresh. `MainViewModel` has no existing unit tests (it takes a live Avalonia `Window` in its constructor and isn't set up for headless testing — see `BetterUMM/ViewModels/MainViewModel.cs:98`), so this task follows the codebase's existing pattern of verifying view-model behavior by running the app (Task 3 covers wiring the button; verification happens after both tasks via manual run).

**Files:**
- Modify: `BetterUMM/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: `_modService.UninstallMod(ModInfo mod)` (Task 1), `LoadMods()` (existing, `MainViewModel.cs:216`), `ShowMessageAsync(string message, string title = "", Icon icon = Icon.None)` (existing, `MainViewModel.cs:348`), `MessageBoxManager.GetMessageBoxStandard(string title, string message, ButtonEnum buttons, Icon icon)` from `MsBox.Avalonia` (already used via `ShowMessageAsync`), `ButtonResult` enum from `MsBox.Avalonia.Enums`.
- Produces: `public ICommand DeleteModCommand { get; }` — later bound in `MainWindow.axaml` (Task 3) with `CommandParameter="{Binding}"` from a `ModInfo` row, so the command's parameter is a `ModInfo` instance (or `null` if unset).

- [ ] **Step 1: Add the `using` for `ButtonResult` if not already present**

Check the top of `BetterUMM/ViewModels/MainViewModel.cs` — it already has:
```csharp
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
```
`ButtonResult` lives in `MsBox.Avalonia.Enums`, so no new `using` is needed.

- [ ] **Step 2: Declare the command property**

In `MainViewModel.cs`, next to the existing command declarations:

```csharp
public ICommand PatchCommand { get; }
public ICommand RefreshCommand { get; }
public ICommand SelectGameCommand { get; }
public ICommand SaveModStatesCommand => _saveModStatesCommand;
public ICommand InstallModCommand { get; }
public ICommand DeleteModCommand { get; }
```

- [ ] **Step 3: Wire the command in the constructor**

In the `MainViewModel` constructor, next to the existing command assignments:

```csharp
PatchCommand          = new RelayCommand(async _ => await PatchSelectedGameAsync());
RefreshCommand        = new RelayCommand(_ => LoadMods());
SelectGameCommand     = new RelayCommand(async _ => await SelectGameAsync());
_saveModStatesCommand = new RelayCommand(async _ => await SaveModStatesAsync(), _ => HasUnsavedChanges);
InstallModCommand     = new RelayCommand(async _ => await InstallModAsync());
DeleteModCommand      = new RelayCommand(async param => await DeleteModAsync(param as ModInfo));
```

- [ ] **Step 4: Implement `DeleteModAsync`**

Add this method near `InstallModAsync` (after it, before `PatchSelectedGameAsync`):

```csharp
private async Task DeleteModAsync(ModInfo? mod)
{
    if (mod == null) return;

    var confirmBox = MessageBoxManager.GetMessageBoxStandard(
        "Delete Mod",
        $"Delete '{mod.DisplayName}'? This will permanently remove its folder and cannot be undone.",
        ButtonEnum.YesNo,
        Icon.Warning);
    var result = await confirmBox.ShowAsync();
    if (result != ButtonResult.Yes) return;

    try
    {
        _modService.UninstallMod(mod);
        LoadMods();
        await ShowMessageAsync($"'{mod.DisplayName}' was deleted.", "Deleted", Icon.Info);
    }
    catch (Exception ex)
    {
        await ShowMessageAsync($"Failed to delete mod: {ex.Message}", "Error", Icon.Error);
    }
}
```

- [ ] **Step 5: Build to confirm it compiles**

Run: `dotnet build BetterUMM/BetterUMM.csproj`
Expected: build succeeds with no errors.

- [ ] **Step 6: Commit**

```bash
git add BetterUMM/ViewModels/MainViewModel.cs
git commit -m "feat: add DeleteModCommand to MainViewModel with confirmation dialog"
```

---

### Task 3: Delete button in the mod list UI

Add the row-level delete button and confirm the end-to-end flow by running the app.

**Files:**
- Modify: `BetterUMM/MainWindow.axaml:76-133` (the mod list header `Grid` and `ListBox`)

**Interfaces:**
- Consumes: `DeleteModCommand` (Task 2), `models:ModInfo` (existing, already used as `x:DataType` in the `DataTemplate`).
- Produces: nothing consumed by later tasks — this is the last task.

- [ ] **Step 1: Add a column header for the new button**

In `MainWindow.axaml`, the column header `Grid` (around line 82-96) currently has 5 columns (`60`, `*`, `100`, `150`, `20`) for Enabled/Name/Version/Author/dirty-marker. Add a 6th column for the delete button:

```xml
<Border Grid.Row="0" Background="#D8D8D8" BorderBrush="#B0B0B0" BorderThickness="0,0,0,1">
    <Grid Margin="4,4">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="60"/>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="100"/>
            <ColumnDefinition Width="150"/>
            <ColumnDefinition Width="20"/>
            <ColumnDefinition Width="30"/>
        </Grid.ColumnDefinitions>
        <TextBlock Grid.Column="0" Text="Enabled" FontWeight="SemiBold" HorizontalAlignment="Center"/>
        <TextBlock Grid.Column="1" Text="Name" FontWeight="SemiBold" Margin="4,0"/>
        <TextBlock Grid.Column="2" Text="Version" FontWeight="SemiBold" Margin="4,0"/>
        <TextBlock Grid.Column="3" Text="Author" FontWeight="SemiBold" Margin="4,0"/>
    </Grid>
</Border>
```

(Only the `Grid.ColumnDefinitions` changes — one `ColumnDefinition Width="30"` added. No new header text needed for the button column.)

- [ ] **Step 2: Add the delete button to the row template**

Replace the row `DataTemplate`'s `Grid` (around line 108-128) — add the matching 6th column definition and a delete `Button` in it:

```xml
<DataTemplate x:DataType="models:ModInfo">
    <Border BorderBrush="#E0E0E0" BorderThickness="0,0,0,1" Padding="4,4">
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="60"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="100"/>
                <ColumnDefinition Width="150"/>
                <ColumnDefinition Width="20"/>
                <ColumnDefinition Width="30"/>
            </Grid.ColumnDefinitions>
            <CheckBox Grid.Column="0" IsChecked="{Binding IsEnabled}"
                      HorizontalAlignment="Center" VerticalAlignment="Center"/>
            <TextBlock Grid.Column="1" Text="{Binding DisplayName}"
                       VerticalAlignment="Center" Margin="4,0" TextTrimming="CharacterEllipsis"/>
            <TextBlock Grid.Column="2" Text="{Binding Version}"
                       VerticalAlignment="Center" Margin="4,0"/>
            <TextBlock Grid.Column="3" Text="{Binding Author}"
                       VerticalAlignment="Center" Margin="4,0" TextTrimming="CharacterEllipsis"/>
            <Ellipse Grid.Column="4" Width="8" Height="8" Fill="Orange"
                     ToolTip.Tip="Unsaved changes"
                     IsVisible="{Binding IsDirty}"
                     VerticalAlignment="Center" HorizontalAlignment="Center"/>
            <Button Grid.Column="5" Content="✕" Padding="4,0"
                    ToolTip.Tip="Delete mod"
                    Command="{Binding $parent[ListBox].((vm:MainViewModel)DataContext).DeleteModCommand}"
                    CommandParameter="{Binding}"
                    HorizontalAlignment="Center" VerticalAlignment="Center"/>
        </Grid>
    </Border>
</DataTemplate>
```

Note: the `DataTemplate`'s `x:DataType` is `models:ModInfo`, so `{Binding}` inside it resolves against the `ModInfo` row — that's correct for `CommandParameter`. But `DeleteModCommand` lives on `MainViewModel`, not `ModInfo`, so `Command` must walk up to the `ListBox`'s ancestor `DataContext` (the window's `MainViewModel`) via `$parent[ListBox].((vm:MainViewModel)DataContext)`, exactly like `MainWindow.axaml:9` declares `xmlns:vm="clr-namespace:BetterUMM.ViewModels"` (already present in this file's root `Window` element) — no new namespace import is needed.

- [ ] **Step 3: Run the app and verify the flow manually**

Run: `dotnet run --project BetterUMM/BetterUMM.csproj`

Manual check (no automated UI test harness exists in this codebase for Avalonia views):
1. Select a game with at least one installed mod (or install one via "Install Mod").
2. Confirm each mod row now shows a "✕" button.
3. Click "✕" on a mod — a "Delete Mod" Yes/No dialog should appear.
4. Click "No" — mod list should be unchanged.
5. Click "✕" again, click "Yes" — a "Deleted" confirmation should appear, the mod should disappear from the list, and its folder should no longer exist on disk.

- [ ] **Step 4: Run the full test suite to check for regressions**

Run: `dotnet test BetterUMM.Tests/BetterUMM.Tests.csproj`
Expected: all tests PASS, including the two new `ModServiceTests` from Task 1.

- [ ] **Step 5: Commit**

```bash
git add BetterUMM/MainWindow.axaml
git commit -m "feat: add per-row delete button to the mod list"
```
