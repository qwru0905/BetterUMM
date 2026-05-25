using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using BetterUMM.Models;
using Newtonsoft.Json;

namespace BetterUMM.Services
{
    public class ModService
    {
        private const string UmmParamsRelPath = @"Managed\UnityModManager\Params.xml";

        public static string GetParamsPath(string gameDataPath)
            => Path.Combine(gameDataPath, UmmParamsRelPath);

        public List<ModInfo> ScanMods(string modsFolderPath, string paramsXmlPath)
        {
            var mods = new List<ModInfo>();
            if (!Directory.Exists(modsFolderPath)) return mods;

            var enabledStates = ReadAllEnabledStates(paramsXmlPath);

            foreach (var dir in Directory.GetDirectories(modsFolderPath))
            {
                string infoPath = Path.Combine(dir, "Info.json");
                if (!File.Exists(infoPath)) continue;

                try
                {
                    var json = File.ReadAllText(infoPath);
                    var mod = JsonConvert.DeserializeObject<ModInfo>(json);
                    if (mod == null) continue;

                    mod.FolderPath = dir;
                    mod.IsEnabled = enabledStates.TryGetValue(mod.Id, out bool enabled) ? enabled : true;
                    mods.Add(mod);
                }
                catch (Exception ex)
                {
                    LoggerService.LogException(ex, $"ScanMods: {dir}");
                }
            }
            return mods;
        }

        private Dictionary<string, bool> ReadAllEnabledStates(string paramsXmlPath)
        {
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(paramsXmlPath)) return result;

            try
            {
                var doc = XDocument.Load(paramsXmlPath);
                var modParams = doc.Root?.Element("ModParams");
                if (modParams == null) return result;

                foreach (var mod in modParams.Elements("Mod"))
                {
                    string? id = (string?)mod.Attribute("Id");
                    string? enabledStr = (string?)mod.Attribute("Enabled");
                    if (id != null)
                        result[id] = !string.Equals(enabledStr, "false", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, $"ReadAllEnabledStates: {paramsXmlPath}");
            }
            return result;
        }

        public void SaveAllEnabledStates(IEnumerable<ModInfo> mods, string paramsXmlPath)
        {
            XDocument doc;
            if (File.Exists(paramsXmlPath))
            {
                doc = XDocument.Load(paramsXmlPath);
            }
            else
            {
                XNamespace xsd = "http://www.w3.org/2001/XMLSchema";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
                doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    new XElement("Param",
                        new XAttribute(XNamespace.Xmlns + "xsd", xsd),
                        new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                        new XElement("ModParams")
                    )
                );
                Directory.CreateDirectory(Path.GetDirectoryName(paramsXmlPath)!);
            }

            var root = doc.Root!;
            var modParams = root.Element("ModParams") ?? new XElement("ModParams");
            if (root.Element("ModParams") == null)
                root.Add(modParams);

            foreach (var mod in mods)
            {
                var existing = modParams.Elements("Mod")
                    .FirstOrDefault(e => string.Equals((string?)e.Attribute("Id"), mod.Id, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    existing.SetAttributeValue("Enabled", mod.IsEnabled ? "true" : "false");
                }
                else
                {
                    modParams.Add(new XElement("Mod",
                        new XAttribute("Id", mod.Id),
                        new XAttribute("Enabled", mod.IsEnabled ? "true" : "false"),
                        new XElement("Hotkey",
                            new XElement("keyCode", "None"),
                            new XElement("modifiers", "0")
                        )
                    ));
                }
            }

            doc.Save(paramsXmlPath);
        }

        public void InstallMod(string zipPath, string modsFolderPath)
        {
            if (!File.Exists(zipPath))
                throw new FileNotFoundException("zip file not found.", zipPath);

            if (!Directory.Exists(modsFolderPath))
                Directory.CreateDirectory(modsFolderPath);

            ZipFile.ExtractToDirectory(zipPath, modsFolderPath, overwriteFiles: true);
        }

        public void UninstallMod(ModInfo mod)
        {
            if (Directory.Exists(mod.FolderPath))
                Directory.Delete(mod.FolderPath, true);
        }
    }
}
