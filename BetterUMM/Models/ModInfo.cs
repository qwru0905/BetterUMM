using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BetterUMM.Models
{
    public class ModInfo : INotifyPropertyChanged
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string ManagerVersion { get; set; } = string.Empty;
        public List<string> Requirements { get; set; } = new();
        public string FolderPath { get; set; } = string.Empty;

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsDirty));
                }
            }
        }

        // 원본 상태와 비교해서 변경 여부 추적
        private bool _savedIsEnabled = true;
        public bool IsDirty => _isEnabled != _savedIsEnabled;

        /// <summary>
        /// 파일에서 읽어온 초기 상태를 기록합니다.
        /// </summary>
        public void MarkAsClean()
        {
            _savedIsEnabled = _isEnabled;
            OnPropertyChanged(nameof(IsDirty));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}