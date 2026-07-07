using System;
using System.IO;
using BetterUMM.Models;
using BetterUMM.Services;
using Xunit;

namespace BetterUMM.Tests.Services
{
    public class ModServiceTests
    {
        [Fact]
        public void UninstallMod_ForExistingFolder_DeletesFolderAndContents()
        {
            string dir = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(dir, "Info.json"), "{}");
                File.WriteAllText(Path.Combine(dir, "mod.dll"), "fake-dll-bytes");
                var mod = new ModInfo { Id = "test.mod", FolderPath = dir };

                new ModService().UninstallMod(mod);

                Assert.False(Directory.Exists(dir));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void UninstallMod_ForMissingFolder_DoesNotThrow()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"betterumm-missing-{Guid.NewGuid():N}");
            var mod = new ModInfo { Id = "test.mod", FolderPath = dir };

            var exception = Record.Exception(() => new ModService().UninstallMod(mod));

            Assert.Null(exception);
        }

        private static string CreateTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"betterumm-modservice-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
