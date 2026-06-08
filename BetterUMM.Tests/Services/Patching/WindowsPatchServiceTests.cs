using BetterUMM.Models;
using BetterUMM.Services.Patching;
using Xunit;

namespace BetterUMM.Tests.Services.Patching
{
    public class WindowsPatchServiceTests
    {
        [Fact]
        public void GetPatchStatus_ForNonExistentGame_ReturnsNotInstalled()
        {
            var service = new WindowsPatchService();
            var game = new GameInfo
            {
                Name = "Ghost",
                Path = "C:\\NonExistent\\Ghost.exe",
                GameDataPath = "C:\\NonExistent\\Ghost_Data",
                AssemblyName = "Assembly-CSharp.dll",
                PatchTarget = string.Empty,
                Folder = "Ghost",
                ModsDirectory = "Mods",
                ModInfo = "Info.json",
                GameExe = "Ghost.exe",
                EntryPoint = string.Empty,
                StartingPoint = string.Empty,
                UIStartingPoint = string.Empty,
                OldPatchTarget = string.Empty,
                GameVersionPoint = string.Empty,
                MinimalManagerVersion = string.Empty,
                HarmonyVersion = string.Empty
            };

            Assert.Equal(PatchStatus.NotInstalled, service.GetPatchStatus(game));
        }
    }
}
