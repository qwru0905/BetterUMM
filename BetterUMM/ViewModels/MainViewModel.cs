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
using BetterUMM.Services.Patching;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace BetterUMM.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ConfigService _configService = new();
        private readonly ModService _modService = new();
        private readonly IPatchService _patchService = PatchServiceFactory.Create();
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
            PatchStatus.Doorstop          => "Installed (Doorstop)",
            PatchStatus.AssemblyInjection => "Installed (Assembly)",
            _                             => "Not Installed"
        };

        public bool IsUmmInstalled => PatchStatus != PatchStatus.NotInstalled;
        public string PatchButtonText => IsUmmInstalled ? "Uninstall UMM" : "Install UMM";

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
            string path;
            string? appBundlePath = null;

            if (OperatingSystem.IsMacOS())
            {
                var folders = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Game .app Bundle",
                    AllowMultiple = false
                });
                if (folders.Count == 0) return;
                appBundlePath = folders[0].Path.LocalPath;

                try
                {
                    path = MacAppBundleHelper.ResolveExecutablePath(appBundlePath);
                }
                catch (Exception ex)
                {
                    await ShowMessageAsync($"Selected item is not a valid .app bundle: {ex.Message}", "Error", Icon.Error);
                    return;
                }
            }
            else
            {
                var fileTypeFilter = OperatingSystem.IsWindows()
                    ? new[] { new FilePickerFileType("Executable files") { Patterns = new[] { "*.exe" } }, FilePickerFileTypes.All }
                    : new[] { FilePickerFileTypes.All };

                var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Game Executable",
                    AllowMultiple = false,
                    FileTypeFilter = fileTypeFilter
                });
                if (files.Count == 0) return;
                path = files[0].Path.LocalPath;
            }

            string representativePath = appBundlePath ?? path;
            string folderName = Path.GetFileName(Path.GetDirectoryName(representativePath)) ?? "";
            string exeName = Path.GetFileName(representativePath);

            var config = _configService.GetGameConfig(folderName) ?? _configService.GetGameConfigByExe(exeName);

            SelectedGame = config != null
                ? BuildGameInfoFromConfig(config, path, appBundlePath)
                : BuildGameInfoUnknown(path, appBundlePath, folderName, exeName);
        }

        private static GameInfo BuildGameInfoFromConfig(GameConfig config, string exePath, string? appBundlePath = null)
        {
            string representativePath = appBundlePath ?? exePath;
            string exeName = Path.GetFileName(representativePath);
            string gameDataPath = appBundlePath != null
                ? Path.Combine(appBundlePath, "Contents", "Resources", "Data")
                : Path.Combine(Path.GetDirectoryName(exePath)!, $"{Path.GetFileNameWithoutExtension(exePath)}_Data");

            string assemblyName = !string.IsNullOrEmpty(config.EntryPoint) && config.EntryPoint.Split('[', ']').Length > 2
                ? config.EntryPoint.Split('[', ']')[1]
                : "Assembly-CSharp.dll";

            return new GameInfo
            {
                Name             = config.Name,
                Path             = appBundlePath ?? exePath,
                GameDataPath     = gameDataPath,
                AssemblyName     = assemblyName,
                PatchTarget      = config.EntryPoint,
                CurrentPatchMethod = PatchMethod.Doorstop,
                Folder           = config.Folder,
                ModsDirectory    = config.ModsDirectory,
                ModInfo          = config.ModInfo,
                GameExe          = string.IsNullOrEmpty(config.GameExe) ? exeName : config.GameExe,
                EntryPoint       = config.EntryPoint,
                StartingPoint    = config.StartingPoint,
                UIStartingPoint  = config.UIStartingPoint,
                OldPatchTarget   = config.OldPatchTarget,
                GameVersionPoint = config.GameVersionPoint,
                MinimalManagerVersion = config.MinimalManagerVersion,
                HarmonyVersion   = config.HarmonyVersion
            };
        }

        private static GameInfo BuildGameInfoUnknown(string exePath, string? appBundlePath, string folderName, string exeName)
        {
            string gameDataPath = appBundlePath != null
                ? Path.Combine(appBundlePath, "Contents", "Resources", "Data")
                : Path.Combine(Path.GetDirectoryName(exePath)!, $"{Path.GetFileNameWithoutExtension(exePath)}_Data");

            return new GameInfo
            {
                Name          = Path.GetFileNameWithoutExtension(appBundlePath ?? exePath),
                Path          = appBundlePath ?? exePath,
                GameDataPath  = gameDataPath,
                AssemblyName  = "Assembly-CSharp.dll",
                CurrentPatchMethod = PatchMethod.Doorstop,
                Folder        = folderName,
                ModsDirectory = "Mods",
                ModInfo       = "Info.json",
                GameExe       = exeName
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

        private static string GetGameRootDirectory(GameInfo game) =>
            OperatingSystem.IsMacOS() ? game.Path : Path.GetDirectoryName(game.Path)!;

        private void LoadMods()
        {
            if (SelectedGame == null || string.IsNullOrEmpty(SelectedGame.Path)) return;

            Mods.Clear();

            string gameDir = GetGameRootDirectory(SelectedGame);
            string modsPath = Path.Combine(gameDir, SelectedGame.ModsDirectory);
            string paramsPath = ModService.GetParamsPath(SelectedGame.GameDataPath);

            var mods = _modService.ScanMods(modsPath, paramsPath);
            foreach (var mod in mods)
            {
                mod.MarkAsClean();
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
                await ShowMessageAsync($"{dirtyMods.Count} mod state(s) saved.", "Saved", Icon.Info);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync($"Failed to save: {ex.Message}", "Error", Icon.Error);
            }
        }

        private async Task InstallModAsync()
        {
            if (SelectedGame == null || string.IsNullOrEmpty(SelectedGame.Path))
            {
                await ShowMessageAsync("Please select a game first.", "No Game Selected", Icon.Warning);
                return;
            }

            var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Mod Zip File",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Zip files") { Patterns = new[] { "*.zip" } } }
            });

            if (files.Count == 0) return;

            string gameDir = GetGameRootDirectory(SelectedGame);
            string modsPath = Path.Combine(gameDir, SelectedGame.ModsDirectory);

            try
            {
                _modService.InstallMod(files[0].Path.LocalPath, modsPath);
                LoadMods();
                await ShowMessageAsync("Mod installed successfully.", "Installed", Icon.Info);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync($"Installation failed: {ex.Message}", "Error", Icon.Error);
            }
        }

        private async Task PatchSelectedGameAsync()
        {
            if (SelectedGame == null || string.IsNullOrEmpty(SelectedGame.Path))
            {
                await ShowMessageAsync("Please select a game first.", "No Game Selected", Icon.Warning);
                return;
            }

            bool ok;
            if (IsUmmInstalled)
            {
                ok = PatchStatus == PatchStatus.Doorstop
                    ? _patchService.RemoveDoorstop(SelectedGame)
                    : _patchService.RemoveAssembly(SelectedGame);

                if (ok) RefreshPatchStatus();
                await ShowMessageAsync(ok ? "Uninstalled successfully!" : "Uninstall failed. Check logs.");
                return;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string ummSourceDir = Path.Combine(baseDir, "UnityModManager");
            if (!Directory.Exists(ummSourceDir))
            {
                await ShowMessageAsync($"UnityModManager resource folder not found:\n{ummSourceDir}", "Resource Not Found", Icon.Error);
                return;
            }

            string[] libs = Directory.GetFiles(ummSourceDir, "*", SearchOption.AllDirectories);
            if (libs.Length == 0)
            {
                await ShowMessageAsync("UnityModManager library files not found.", "Resource Not Found", Icon.Error);
                return;
            }

            if (SelectedGame.CurrentPatchMethod == PatchMethod.Doorstop)
                ok = _patchService.InstallDoorstop(SelectedGame, libs);
            else
                ok = _patchService.InstallAssembly(SelectedGame, libs);

            if (ok)
            {
                string gameDir = GetGameRootDirectory(SelectedGame);
                string modsPath = Path.Combine(gameDir, SelectedGame.ModsDirectory);
                if (!Directory.Exists(modsPath))
                    Directory.CreateDirectory(modsPath);

                RefreshPatchStatus();
            }

            string logPath = Path.Combine(baseDir, "logs");
            await ShowMessageAsync(
                ok ? "Patch successful!" : $"Patch failed. Check logs.\nLog location: {logPath}",
                ok ? "Done" : "Error",
                ok ? Icon.Info : Icon.Error);
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
