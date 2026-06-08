using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using BetterUMM.Models;

namespace BetterUMM.Services.Patching
{
    public static class PatchFileOps
    {
        public static void ExportConfig(GameInfo game, string destPath)
        {
            var config = new UmmConfig
            {
                Name = game.Name,
                Folder = game.Folder,
                ModsDirectory = game.ModsDirectory,
                ModInfo = game.ModInfo,
                GameExe = game.GameExe,
                EntryPoint = game.EntryPoint,
                StartingPoint = game.StartingPoint,
                UIStartingPoint = game.UIStartingPoint,
                MinimalManagerVersion = game.MinimalManagerVersion,
            };
            var serializer = new XmlSerializer(typeof(UmmConfig));
            using var writer = new StreamWriter(destPath);
            serializer.Serialize(writer, config);
        }

        public static void MakeBackup(string path, List<string> tracked)
        {
            if (!File.Exists(path)) return;
            File.Copy(path, path + ".bak", true);
            tracked.Add(path);
        }

        public static void RestoreBackups(List<string> tracked)
        {
            foreach (var path in tracked)
                if (File.Exists(path + ".bak"))
                    File.Move(path + ".bak", path, true);
        }

        public static void DeleteBackups(List<string> tracked)
        {
            foreach (var path in tracked)
                TryDelete(path + ".bak");
        }

        public static void TryDelete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [XmlRoot("Config")]
    public class UmmConfig
    {
        [XmlAttribute("Name")]
        public string Name { get; set; } = string.Empty;
        [XmlElement("Folder")]
        public string Folder { get; set; } = string.Empty;
        [XmlElement("ModsDirectory")]
        public string ModsDirectory { get; set; } = "Mods";
        [XmlElement("ModInfo")]
        public string ModInfo { get; set; } = "Info.json";
        [XmlElement("GameExe")]
        public string GameExe { get; set; } = string.Empty;
        [XmlElement("EntryPoint")]
        public string EntryPoint { get; set; } = string.Empty;
        [XmlElement("StartingPoint")]
        public string StartingPoint { get; set; } = string.Empty;
        [XmlElement("UIStartingPoint")]
        public string UIStartingPoint { get; set; } = string.Empty;
        [XmlElement("MinimalManagerVersion")]
        public string MinimalManagerVersion { get; set; } = string.Empty;
    }
}
