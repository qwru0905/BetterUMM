using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using BetterUMM.Models;

namespace BetterUMM.Services
{
    public class ConfigService
    {
        private const string ConfigFileName = "UnityModManagerConfig.xml";
        private List<GameConfig> _gameConfigs = new();

        public void LoadConfig()
        {
            if (!File.Exists(ConfigFileName))
            {
                // In a real scenario, we might download this or have a default.
                return;
            }

            try
            {
                var serializer = new XmlSerializer(typeof(ConfigRoot));
                using (var reader = new StreamReader(ConfigFileName))
                {
                    var root = (ConfigRoot)serializer.Deserialize(reader)!;
                    _gameConfigs = root.GameConfigs;
                }
            }
            catch (Exception ex)
            {
                // Handle error (e.g., logging)
                Console.WriteLine($"Error loading config: {ex.Message}");
            }
        }

        public GameConfig? GetGameConfig(string gameFolderName)
        {
            return _gameConfigs.Find(c => c.Folder.Equals(gameFolderName, StringComparison.OrdinalIgnoreCase));
        }

        public List<GameConfig> GetAllConfigs() => _gameConfigs;
    }

    [XmlRoot("Config")]
    public class ConfigRoot
    {
        public string Repository { get; set; } = string.Empty;
        [XmlElement("GameInfo")]
        public List<GameConfig> GameConfigs { get; set; } = new();
    }

    public class GameConfig
    {
        [XmlElement("Name")]
        public string Name { get; set; } = string.Empty;
        [XmlElement("Folder")]
        public string Folder { get; set; } = string.Empty;
        [XmlElement("ModsDirectory")]
        public string ModsDirectory { get; set; } = "Mods";
        [XmlElement("ModInfo")]
        public string ModInfo { get; set; } = "Info.json";
        [XmlElement("AssemblyName")]
        public string AssemblyName { get; set; } = "Assembly-CSharp.dll";
        [XmlElement("PatchTarget")]
        public string PatchTarget { get; set; } = string.Empty;
    }
}
