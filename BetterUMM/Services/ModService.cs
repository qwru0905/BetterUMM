using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BetterUMM.Models;
using Newtonsoft.Json;

namespace BetterUMM.Services
{
    public class ModService
    {
        public List<ModInfo> ScanMods(string modsFolderPath)
        {
            var mods = new List<ModInfo>();
            if (!Directory.Exists(modsFolderPath)) return mods;

            foreach (var dir in Directory.GetDirectories(modsFolderPath))
            {
                string infoPath = Path.Combine(dir, "Info.json");
                if (File.Exists(infoPath))
                {
                    try
                    {
                        var json = File.ReadAllText(infoPath);
                        var mod = JsonConvert.DeserializeObject<ModInfo>(json);
                        if (mod != null)
                        {
                            mod.FolderPath = dir;
                            mods.Add(mod);
                        }
                    }
                    catch { /* Ignore invalid JSON */ }
                }
            }
            return mods;
        }

        public void ToggleMod(ModInfo mod, bool enabled)
        {
            string infoPath = Path.Combine(mod.FolderPath, "Info.json");
            if (File.Exists(infoPath))
            {
                mod.IsEnabled = enabled;
                var json = JsonConvert.SerializeObject(mod, Formatting.Indented);
                File.WriteAllText(infoPath, json);
            }
        }

        public void InstallMod(string zipPath, string modsFolderPath)
        {
            // Simple extraction logic
            ZipFile.ExtractToDirectory(zipPath, modsFolderPath, true);
        }

        public void UninstallMod(ModInfo mod)
        {
            if (Directory.Exists(mod.FolderPath))
            {
                Directory.Delete(mod.FolderPath, true);
            }
        }
    }
}
