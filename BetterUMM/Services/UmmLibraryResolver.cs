using System.IO;
using System.Linq;
using BetterUMM.Models;

namespace BetterUMM.Services
{
    public static class UmmLibraryResolver
    {
        private const string MainDirName = "UnityModManager";
        private const string VanillaDirName = "UnityModManagerVanilla";
        private static readonly string[] VariantFileNames = { "UnityModManager.dll", "UnityModManager.xml" };

        // baseDir: application directory containing the UnityModManager[Vanilla] resource folders.
        public static string[] Resolve(string baseDir, UmmVariant variant)
        {
            string mainDir = Path.Combine(baseDir, MainDirName);
            if (!Directory.Exists(mainDir))
                throw new DirectoryNotFoundException($"UnityModManager resource folder not found: {mainDir}");

            var libs = Directory.GetFiles(mainDir, "*", SearchOption.AllDirectories);
            if (variant == UmmVariant.Modified)
                return libs;

            string vanillaDir = Path.Combine(baseDir, VanillaDirName);
            if (!Directory.Exists(vanillaDir))
                throw new DirectoryNotFoundException($"Vanilla UnityModManager folder not found: {vanillaDir}");

            string vanillaDll = Path.Combine(vanillaDir, "UnityModManager.dll");
            if (!File.Exists(vanillaDll))
                throw new FileNotFoundException("Vanilla UnityModManager.dll not found.", vanillaDll);

            var vanillaFiles = VariantFileNames
                .Select(n => Path.Combine(vanillaDir, n))
                .Where(File.Exists)
                .ToArray();

            return libs
                .Where(p => !VariantFileNames.Contains(Path.GetFileName(p)))
                .Concat(vanillaFiles)
                .ToArray();
        }
    }
}
