using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using BetterUMM.Models;
using BetterUMM.Services;

namespace BetterUMM.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ConfigService _configService = new();
        private readonly ModService _modService = new();
        private readonly PatchService _patchService = new();
        private readonly ProfileService _profileService = new();

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
            PatchStatus.Doorstop         => "설치됨 (Doorstop)",
            PatchStatus.AssemblyInjection => "설치됨 (Assembly)",
            _                             => "미설치"
        };

        public bool IsUmmInstalled => PatchStatus != PatchStatus.NotInstalled;

        public string PatchButtonText => IsUmmInstalled ? "Uninstall UMM" : "Install UMM";

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

        public MainViewModel()
        {
            _configService.LoadConfig();
            foreach (var config in _configService.GetAllConfigs())
            {
                Games.Add(new GameInfo { Name = config.Name, Folder = config.Folder });
            }

            PatchCommand = new RelayCommand(_ => PatchSelectedGame());
            RefreshCommand = new RelayCommand(_ => LoadMods());
            SelectGameCommand = new RelayCommand(_ => SelectGame());
        }

        private void SelectGame()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                Title = "Select Game Executable"
            };

            if (openFileDialog.ShowDialog() != true) return;

            string path = openFileDialog.FileName;
            string folderName = Path.GetFileName(Path.GetDirectoryName(path)) ?? "";

            var config = _configService.GetGameConfig(folderName);
            SelectedGame = new GameInfo
            {
                Name = config?.Name ?? Path.GetFileNameWithoutExtension(path),
                Path = path,
                GameDataPath = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}_Data"),
                AssemblyName = config != null ? (config.EntryPoint.Split('[', ']').Length > 2 ? config.EntryPoint.Split('[', ']')[1] : "Assembly-CSharp.dll") : "Assembly-CSharp.dll",
                PatchTarget = config?.EntryPoint ?? string.Empty,
                CurrentPatchMethod = PatchMethod.Doorstop,
                
                Folder = config?.Folder ?? folderName,
                ModsDirectory = config?.ModsDirectory ?? "Mods",
                ModInfo = config?.ModInfo ?? "Info.json",
                GameExe = config?.GameExe ?? Path.GetFileName(path),
                EntryPoint = config?.EntryPoint ?? string.Empty,
                StartingPoint = config?.StartingPoint ?? string.Empty,
                UIStartingPoint = config?.UIStartingPoint ?? string.Empty,
                OldPatchTarget = config?.OldPatchTarget ?? string.Empty,
                GameVersionPoint = config?.GameVersionPoint ?? string.Empty,
                MinimalManagerVersion = config?.MinimalManagerVersion ?? string.Empty,
                HarmonyVersion = config?.HarmonyVersion ?? string.Empty
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
            string modsPath = Path.Combine(Path.GetDirectoryName(SelectedGame.Path)!, "Mods");
            var mods = _modService.ScanMods(modsPath);
            foreach (var mod in mods) 
            {
                Mods.Add(mod);
            }
        }

        private void PatchSelectedGame()
        {
            if (SelectedGame == null || string.IsNullOrEmpty(SelectedGame.Path)) return;

            bool ok;
            if (IsUmmInstalled)
            {
                if (PatchStatus == PatchStatus.Doorstop)
                {
                    ok = _patchService.RemoveDoorstop(SelectedGame);
                }
                else
                {
                    ok = _patchService.RemoveAssembly(SelectedGame);
                }

                if (ok) RefreshPatchStatus();
                System.Windows.MessageBox.Show(ok ? "제거 성공!" : "제거 실패. 로그를 확인하세요.");
                return;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string ummSourceDir = Path.Combine(baseDir, "UnityModManager");
            if (!Directory.Exists(ummSourceDir))
            {
                System.Windows.MessageBox.Show($"UnityModManager 리소스 폴더를 찾을 수 없습니다: {ummSourceDir}");
                return;
            }

            string[] libs = Directory.GetFiles(ummSourceDir, "*", SearchOption.AllDirectories);
            if (libs.Length == 0)
            {
                System.Windows.MessageBox.Show("UnityModManager 라이브러리 파일을 찾을 수 없습니다.");
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
            System.Windows.MessageBox.Show(ok ? "패치 성공!" : "패치 실패. 로그를 확인하세요.");
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
