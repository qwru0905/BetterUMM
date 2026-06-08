using System;
using System.IO;
using BetterUMM.Models;
using BetterUMM.Services;

namespace BetterUMM.Services.Patching
{
    public class UnixDoorstopPatchService : IPatchService
    {
        private const string LibSoName = "libdoorstop.so";
        private const string LibDylibName = "libdoorstop.dylib";
        private const string OfficialRunScriptName = "run.sh";
        private const string WrapperScriptName = "run_umm.sh";
        private const string UmmSubDir = "UnityModManager";
        private const string UmmDllName = "UnityModManager.dll";

        private const UnixFileMode ExecutableMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

        public PatchStatus GetPatchStatus(GameInfo game)
        {
            var (_, targetDir) = ResolvePaths(game);
            string libName = OperatingSystem.IsMacOS() ? LibDylibName : LibSoName;
            bool hasLib = File.Exists(Path.Combine(targetDir, libName));
            bool hasWrapper = File.Exists(Path.Combine(targetDir, WrapperScriptName));
            return hasLib && hasWrapper ? PatchStatus.Doorstop : PatchStatus.NotInstalled;
        }

        public bool InstallDoorstop(GameInfo game, string[] ummLibraryPaths)
        {
            var (executablePath, targetDir) = ResolvePaths(game);
            string managedPath = Path.Combine(game.GameDataPath, "Managed");
            string ummDir = Path.Combine(managedPath, UmmSubDir);
            string targetAssemblyPath = Path.Combine(ummDir, UmmDllName);
            string gameConfigPath = Path.Combine(ummDir, "Config.xml");

            try
            {
                if (!Directory.Exists(ummDir))
                    Directory.CreateDirectory(ummDir);

                string doorstopResourceDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Doorstop");
                string libDestPath = CopyNativeLibrary(executablePath, doorstopResourceDir, targetDir);

                string runScriptDest = Path.Combine(targetDir, OfficialRunScriptName);
                File.Copy(Path.Combine(doorstopResourceDir, "unix", OfficialRunScriptName), runScriptDest, true);

                string wrapperDest = Path.Combine(targetDir, WrapperScriptName);
                File.WriteAllText(wrapperDest, BuildWrapperScriptContent(targetDir, executablePath, targetAssemblyPath));

                File.SetUnixFileMode(libDestPath, ExecutableMode);
                File.SetUnixFileMode(runScriptDest, ExecutableMode);
                File.SetUnixFileMode(wrapperDest, ExecutableMode);

                foreach (var lib in ummLibraryPaths)
                    File.Copy(lib, Path.Combine(ummDir, Path.GetFileName(lib)), true);

                PatchFileOps.ExportConfig(game, gameConfigPath);

                LoggerService.Log(
                    "Doorstop이 설치되었습니다. 패치된 상태로 게임을 실행하려면 게임 폴더의 'run_umm.sh'를 사용하세요. " +
                    "Steam: 라이브러리 > 속성 > 실행 옵션에 './run_umm.sh %command%' 입력 / " +
                    "비-Steam: 터미널에서 './run_umm.sh' 실행.");
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "UnixDoorstopPatchService.InstallDoorstop");
                return false;
            }
        }

        public bool RemoveDoorstop(GameInfo game)
        {
            var (_, targetDir) = ResolvePaths(game);
            string ummDir = Path.Combine(game.GameDataPath, "Managed", UmmSubDir);
            string libName = OperatingSystem.IsMacOS() ? LibDylibName : LibSoName;
            try
            {
                PatchFileOps.TryDelete(Path.Combine(targetDir, libName));
                PatchFileOps.TryDelete(Path.Combine(targetDir, OfficialRunScriptName));
                PatchFileOps.TryDelete(Path.Combine(targetDir, WrapperScriptName));
                if (Directory.Exists(ummDir))
                    Directory.Delete(ummDir, true);
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "UnixDoorstopPatchService.RemoveDoorstop");
                return false;
            }
        }

        public bool InstallAssembly(GameInfo game, string[] ummLibraryPaths) =>
            throw new NotSupportedException("Assembly Injection 방식은 현재 Windows에서만 지원됩니다.");

        public bool RemoveAssembly(GameInfo game) =>
            throw new NotSupportedException("Assembly Injection 방식은 현재 Windows에서만 지원됩니다.");

        private static (string ExecutablePath, string TargetDir) ResolvePaths(GameInfo game)
        {
            string executablePath = OperatingSystem.IsMacOS()
                ? MacAppBundleHelper.ResolveExecutablePath(game.Path)
                : game.Path;
            string targetDir = OperatingSystem.IsMacOS() ? game.Path : Path.GetDirectoryName(executablePath)!;
            return (executablePath, targetDir);
        }

        private static string CopyNativeLibrary(string executablePath, string doorstopResourceDir, string targetDir)
        {
            string sourcePath;
            string destFileName;
            if (OperatingSystem.IsMacOS())
            {
                sourcePath = Path.Combine(doorstopResourceDir, "osx", LibDylibName);
                destFileName = LibDylibName;
            }
            else
            {
                bool? is64 = ElfBinaryInspector.Is64Bit(executablePath);
                string arch = is64 == false ? "x86" : "x64";
                sourcePath = Path.Combine(doorstopResourceDir, "linux", arch, LibSoName);
                destFileName = LibSoName;
            }
            string destPath = Path.Combine(targetDir, destFileName);
            File.Copy(sourcePath, destPath, true);
            return destPath;
        }

        public static string BuildWrapperScriptContent(string targetDir, string executablePath, string targetAssemblyPath)
        {
            string relativeExecutablePath = "./" + Path.GetRelativePath(targetDir, executablePath).Replace('\\', '/');
            return "#!/bin/sh\n" +
                $"exec \"$(dirname \"$0\")/{OfficialRunScriptName}\" \\\n" +
                $"    \"{relativeExecutablePath}\" \\\n" +
                $"    --doorstop-target-assembly \"{targetAssemblyPath}\"\n";
        }
    }
}
