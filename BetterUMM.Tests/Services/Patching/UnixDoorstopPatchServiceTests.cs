using BetterUMM.Services.Patching;
using Xunit;

namespace BetterUMM.Tests.Services.Patching
{
    public class UnixDoorstopPatchServiceTests
    {
        [Fact]
        public void BuildWrapperScriptContent_ForLinuxLayout_GeneratesRelativeExecPath()
        {
            string content = UnixDoorstopPatchService.BuildWrapperScriptContent(
                targetDir: "/home/user/Games/MyGame",
                executablePath: "/home/user/Games/MyGame/MyGame",
                targetAssemblyPath: "/home/user/Games/MyGame/MyGame_Data/Managed/UnityModManager/UnityModManager.dll");

            Assert.Equal(
                "#!/bin/sh\n" +
                "exec \"$(dirname \"$0\")/run.sh\" \\\n" +
                "    \"./MyGame\" \\\n" +
                "    --doorstop-target-assembly \"/home/user/Games/MyGame/MyGame_Data/Managed/UnityModManager/UnityModManager.dll\"\n",
                content);
        }

        [Fact]
        public void BuildWrapperScriptContent_ForMacAppBundleLayout_UsesNestedRelativePath()
        {
            string content = UnixDoorstopPatchService.BuildWrapperScriptContent(
                targetDir: "/Users/foo/Games/MyGame.app",
                executablePath: "/Users/foo/Games/MyGame.app/Contents/MacOS/MyGame",
                targetAssemblyPath: "/Users/foo/Games/MyGame.app/Contents/Resources/Data/Managed/UnityModManager/UnityModManager.dll");

            Assert.Contains("\"./Contents/MacOS/MyGame\"", content);
            Assert.StartsWith("#!/bin/sh\n", content);
            Assert.Contains("--doorstop-target-assembly \"/Users/foo/Games/MyGame.app/Contents/Resources/Data/Managed/UnityModManager/UnityModManager.dll\"", content);
        }
    }
}
