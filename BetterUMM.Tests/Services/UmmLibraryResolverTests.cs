using BetterUMM.Models;
using BetterUMM.Services;
using Xunit;

namespace BetterUMM.Tests.Services
{
    public class UmmLibraryResolverTests : IDisposable
    {
        private readonly string _baseDir = Path.Combine(Path.GetTempPath(), "umm-resolver-" + Guid.NewGuid().ToString("N"));

        public UmmLibraryResolverTests()
        {
            Directory.CreateDirectory(Path.Combine(_baseDir, "UnityModManager"));
            foreach (var f in new[] { "UnityModManager.dll", "UnityModManager.xml", "0Harmony.dll", "dnlib.dll" })
                File.WriteAllText(Path.Combine(_baseDir, "UnityModManager", f), "modified");
        }

        public void Dispose() => Directory.Delete(_baseDir, true);

        private void CreateVanilla()
        {
            string dir = Path.Combine(_baseDir, "UnityModManagerVanilla");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "UnityModManager.dll"), "vanilla");
            File.WriteAllText(Path.Combine(dir, "UnityModManager.xml"), "vanilla");
        }

        [Fact]
        public void Resolve_Modified_ReturnsAllFilesFromMainFolder()
        {
            var libs = UmmLibraryResolver.Resolve(_baseDir, UmmVariant.Modified);

            Assert.Equal(4, libs.Length);
            Assert.All(libs, p => Assert.Equal("modified", File.ReadAllText(p)));
        }

        [Fact]
        public void Resolve_Vanilla_ReplacesOnlyUmmDllAndXml()
        {
            CreateVanilla();

            var libs = UmmLibraryResolver.Resolve(_baseDir, UmmVariant.Vanilla);

            Assert.Equal(4, libs.Length);
            Assert.Equal("vanilla", File.ReadAllText(libs.Single(p => Path.GetFileName(p) == "UnityModManager.dll")));
            Assert.Equal("vanilla", File.ReadAllText(libs.Single(p => Path.GetFileName(p) == "UnityModManager.xml")));
            Assert.Equal("modified", File.ReadAllText(libs.Single(p => Path.GetFileName(p) == "0Harmony.dll")));
            Assert.Equal("modified", File.ReadAllText(libs.Single(p => Path.GetFileName(p) == "dnlib.dll")));
        }

        [Fact]
        public void Resolve_Vanilla_WithoutVanillaFolder_Throws()
        {
            Assert.Throws<DirectoryNotFoundException>(() => UmmLibraryResolver.Resolve(_baseDir, UmmVariant.Vanilla));
        }

        [Fact]
        public void Resolve_Vanilla_WithoutVanillaDll_Throws()
        {
            Directory.CreateDirectory(Path.Combine(_baseDir, "UnityModManagerVanilla"));

            Assert.Throws<FileNotFoundException>(() => UmmLibraryResolver.Resolve(_baseDir, UmmVariant.Vanilla));
        }

        [Fact]
        public void Resolve_MissingMainFolder_Throws()
        {
            Directory.Delete(Path.Combine(_baseDir, "UnityModManager"), true);

            Assert.Throws<DirectoryNotFoundException>(() => UmmLibraryResolver.Resolve(_baseDir, UmmVariant.Modified));
        }
    }
}
