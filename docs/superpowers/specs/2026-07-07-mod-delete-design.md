# Mod Delete (Uninstall) — Design

## Problem

The mod list UI (`MainWindow.axaml`) has Install/Refresh/Save actions but no way to
delete/uninstall an installed mod. `ModService.UninstallMod(ModInfo mod)`
(`BetterUMM/Services/ModService.cs:137`) already exists and deletes the mod's
folder, but nothing in the ViewModel or UI calls it — it's dead code.

## Design

**UI (`MainWindow.axaml`)**: add a delete button to each row of the mod
`ListBox.ItemTemplate`, alongside the existing Enabled/Name/Version/Author/dirty-marker
columns. The button's `CommandParameter` binds to the row's `ModInfo`.

**ViewModel (`MainViewModel.cs`)**: add `ICommand DeleteModCommand`, wired to a new
`DeleteModAsync(ModInfo mod)`:
1. Show a Yes/No confirmation dialog via `MsBox.Avalonia`
   (`MessageBoxManager.GetMessageBoxStandard(..., ButtonEnum.YesNo, ...)`), since
   deletion removes the mod's folder from disk and cannot be undone.
2. On "Yes", call `_modService.UninstallMod(mod)`, then `LoadMods()` to refresh the
   list (this also naturally re-subscribes `PropertyChanged` handlers).
3. On failure, show an error message following the existing `ShowMessageAsync`
   catch pattern used by `InstallModAsync`/`PatchSelectedGameAsync`.

**Params.xml**: the deleted mod's `<Mod Id="...">` entry in `Params.xml` is left as
an orphaned entry. It's harmless, and if the same mod is reinstalled later its prior
enabled/disabled state is restored for free. No cleanup logic is added for this.

## Out of scope

- Multi-select delete
- Undo/restore of deleted mods
