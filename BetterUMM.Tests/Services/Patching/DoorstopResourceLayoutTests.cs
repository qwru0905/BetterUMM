using System;
using System.IO;
using Xunit;

namespace BetterUMM.Tests.Services.Patching
{
    public class DoorstopResourceLayoutTests
    {
        [Theory]
        [InlineData("win", "x64", "winhttp.dll")]
        [InlineData("win", "x86", "winhttp.dll")]
        [InlineData("linux", "x64", "libdoorstop.so")]
        [InlineData("linux", "x86", "libdoorstop.so")]
        [InlineData("osx", "", "libdoorstop.dylib")]
        [InlineData("unix", "", "run.sh")]
        public void DoorstopResource_IsCopiedToOutputDirectory(string platform, string arch, string fileName)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string path = string.IsNullOrEmpty(arch)
                ? Path.Combine(baseDir, "Doorstop", platform, fileName)
                : Path.Combine(baseDir, "Doorstop", platform, arch, fileName);

            Assert.True(File.Exists(path), $"Expected resource not found at: {path}");
        }
    }
}
