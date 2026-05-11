using System.Collections.Generic;

namespace BetterUMM.Models
{
    public class ModInfo
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string ManagerVersion { get; set; } = string.Empty;
        public List<string> Requirements { get; set; } = new();
        public bool IsEnabled { get; set; } = true;
        public string FolderPath { get; set; } = string.Empty;
    }
}
