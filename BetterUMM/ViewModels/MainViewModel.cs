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
