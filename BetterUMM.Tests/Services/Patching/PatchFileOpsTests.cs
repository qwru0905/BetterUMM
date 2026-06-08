using System;
using System.Collections.Generic;
using System.IO;
using BetterUMM.Models;
using BetterUMM.Services.Patching;
using Xunit;

namespace BetterUMM.Tests.Services.Patching
{
    public class PatchFileOpsTests
    {
        [Fact]
        public void MakeBackup_CreatesBakFileAndTracksOriginalPath_ThenRestoreMovesItBack()
        {
            string dir = CreateTempDir();
            try
            {
                string original = Path.Combine(dir, "config.ini");
                File.WriteAllText(original, "original");
                var tracked = new List<string>();

                PatchFileOps.MakeBackup(original, tracked);

                Assert.Single(tracked);
                Assert.Equal(original, tracked[0]);
                Assert.True(File.Exists(original + ".bak"));

                File.WriteAllText(original, "modified");
                PatchFileOps.RestoreBackups(tracked);

                Assert.Equal("original", File.ReadAllText(original));
                Assert.False(File.Exists(original + ".bak"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void MakeBackup_ForMissingOriginal_DoesNotTrackOrCreateBackup()
        {
            string dir = CreateTempDir();
            try
            {
                string original = Path.Combine(dir, "missing.ini");
                var tracked = new List<string>();

                PatchFileOps.MakeBackup(original, tracked);

                Assert.Empty(tracked);
                Assert.False(File.Exists(original + ".bak"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void DeleteBackups_RemovesBakFilesForTrackedPaths()
        {
            string dir = CreateTempDir();
            try
            {
                string original = Path.Combine(dir, "config.ini");
                File.WriteAllText(original, "original");
                var tracked = new List<string>();
                PatchFileOps.MakeBackup(original, tracked);

                PatchFileOps.DeleteBackups(tracked);

                Assert.False(File.Exists(original + ".bak"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void TryDelete_ForExistingFile_DeletesIt()
        {
            string dir = CreateTempDir();
            try
            {
                string path = Path.Combine(dir, "victim.txt");
                File.WriteAllText(path, "data");

                PatchFileOps.TryDelete(path);

                Assert.False(File.Exists(path));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void TryDelete_ForMissingFile_DoesNotThrow()
        {
            string path = Path.Combine(Path.GetTempPath(), $"betterumm-missing-{Guid.NewGuid():N}.txt");
            var exception = Record.Exception(() => PatchFileOps.TryDelete(path));
            Assert.Null(exception);
        }

        [Fact]
        public void ExportConfig_WritesXmlFileWithGameNameAndFolder()
        {
            string dir = CreateTempDir();
            try
            {
                string configPath = Path.Combine(dir, "Config.xml");
                var game = new GameInfo
                {
                    Name = "TestGame",
                    Path = "C:\\Games\\TestGame\\TestGame.exe",
                    GameDataPath = "C:\\Games\\TestGame\\TestGame_Data",
                    AssemblyName = "Assembly-CSharp.dll",
                    PatchTarget = string.Empty,
                    Folder = "TestGameFolder",
                    ModsDirectory = "Mods",
                    ModInfo = "Info.json",
                    GameExe = "TestGame.exe",
                    EntryPoint = string.Empty,
                    StartingPoint = string.Empty,
                    UIStartingPoint = string.Empty,
                    OldPatchTarget = string.Empty,
                    GameVersionPoint = string.Empty,
                    MinimalManagerVersion = string.Empty,
                    HarmonyVersion = string.Empty
                };

                PatchFileOps.ExportConfig(game, configPath);

                Assert.True(File.Exists(configPath));
                string content = File.ReadAllText(configPath);
                Assert.Contains("TestGame", content);
                Assert.Contains("TestGameFolder", content);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static string CreateTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"betterumm-fileops-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
