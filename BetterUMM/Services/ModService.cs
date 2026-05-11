using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Xml.Serialization;
using BetterUMM.Models;
using Newtonsoft.Json;

namespace BetterUMM.Services
{
    public class ModService
    {
        private const string ParamsFileName = "Params.xml";

        // Params.xml 구조 (UMM 원본 형식)
        [XmlRoot("Param")]
        public class ModParams
        {
            [XmlElement("Id")]
            public string Id { get; set; } = string.Empty;

            [XmlElement("Enabled")]
            public bool Enabled { get; set; } = true;
        }

        public List<ModInfo> ScanMods(string modsFolderPath)
        {
            var mods = new List<ModInfo>();
            if (!Directory.Exists(modsFolderPath)) return mods;

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

                    // IsEnabled는 Params.xml에서 읽기
                    mod.IsEnabled = ReadEnabledFromParams(dir, mod.Id);

                    mods.Add(mod);
                }
                catch (Exception ex)
                {
                    LoggerService.LogException(ex, $"ScanMods: {dir}");
                }
            }
            return mods;
        }

        private bool ReadEnabledFromParams(string modFolder, string modId)
        {
            string paramsPath = Path.Combine(modFolder, ParamsFileName);
            if (!File.Exists(paramsPath)) return true; // 파일 없으면 기본값 활성화

            try
            {
                var serializer = new XmlSerializer(typeof(ModParams));
                using var reader = new StreamReader(paramsPath);
                var param = (ModParams?)serializer.Deserialize(reader);
                return param?.Enabled ?? true;
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, $"ReadEnabledFromParams: {modFolder}");
                return true;
            }
        }

        /// <summary>
        /// 모드의 활성화 상태를 Params.xml에 저장합니다.
        /// Info.json은 건드리지 않습니다.
        /// </summary>
        public void SaveEnabledState(ModInfo mod)
        {
            string paramsPath = Path.Combine(mod.FolderPath, ParamsFileName);

            var param = new ModParams
            {
                Id = mod.Id,
                Enabled = mod.IsEnabled
            };

            try
            {
                var serializer = new XmlSerializer(typeof(ModParams));
                using var writer = new StreamWriter(paramsPath);
                serializer.Serialize(writer, param);
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, $"SaveEnabledState: {mod.Id}");
                throw new IOException($"Params.xml 저장 실패 ({mod.Id}): {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 변경된 모드 목록의 활성화 상태를 일괄 저장합니다.
        /// </summary>
        public void SaveAllEnabledStates(IEnumerable<ModInfo> mods)
        {
            foreach (var mod in mods)
            {
                SaveEnabledState(mod);
            }
        }

        public void InstallMod(string zipPath, string modsFolderPath)
        {
            if (!File.Exists(zipPath))
                throw new FileNotFoundException("zip 파일을 찾을 수 없습니다.", zipPath);

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