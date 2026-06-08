using System;
using System.IO;
using BetterUMM.Services.Patching;
using Xunit;

namespace BetterUMM.Tests.Services.Patching
{
    public class MacAppBundleHelperTests
    {
        private const string ValidPlist =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
            "<plist version=\"1.0\">\n" +
            "<dict>\n" +
            "    <key>CFBundleExecutable</key>\n" +
            "    <string>MyGame</string>\n" +
            "</dict>\n" +
            "</plist>\n";

        [Fact]
        public void ResolveExecutablePath_ForValidBundle_ReturnsExecutablePath()
        {
            string bundlePath = CreateBundle("MyGame.app", ValidPlist);
            try
            {
                string result = MacAppBundleHelper.ResolveExecutablePath(bundlePath);
                Assert.Equal(Path.Combine(bundlePath, "Contents", "MacOS", "MyGame"), result);
            }
            finally
            {
                Directory.Delete(bundlePath, true);
            }
        }

        [Fact]
        public void ResolveExecutablePath_ForMissingInfoPlist_ThrowsFileNotFoundException()
        {
            string bundlePath = Path.Combine(Path.GetTempPath(), $"betterumm-bundle-{Guid.NewGuid():N}", "Empty.app");
            Directory.CreateDirectory(Path.Combine(bundlePath, "Contents"));
            try
            {
                Assert.Throws<FileNotFoundException>(() => MacAppBundleHelper.ResolveExecutablePath(bundlePath));
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(bundlePath)!, true);
            }
        }

        [Fact]
        public void ResolveExecutablePath_ForPlistWithoutExecutableKey_ThrowsInvalidDataException()
        {
            const string plistWithoutKey =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<plist version=\"1.0\">\n" +
                "<dict>\n" +
                "    <key>CFBundleName</key>\n" +
                "    <string>MyGame</string>\n" +
                "</dict>\n" +
                "</plist>\n";
            string bundlePath = CreateBundle("NoExec.app", plistWithoutKey);
            try
            {
                Assert.Throws<InvalidDataException>(() => MacAppBundleHelper.ResolveExecutablePath(bundlePath));
            }
            finally
            {
                Directory.Delete(bundlePath, true);
            }
        }

        [Fact]
        public void ResolveExecutablePath_ForBinaryPlist_ThrowsNotSupportedException()
        {
            string bundlePath = Path.Combine(Path.GetTempPath(), $"betterumm-bundle-{Guid.NewGuid():N}", "Binary.app");
            Directory.CreateDirectory(Path.Combine(bundlePath, "Contents"));
            File.WriteAllBytes(Path.Combine(bundlePath, "Contents", "Info.plist"), new byte[] { (byte)'b', (byte)'p', (byte)'l', (byte)'i', (byte)'s', (byte)'t', 0x30, 0x30 });
            try
            {
                Assert.Throws<NotSupportedException>(() => MacAppBundleHelper.ResolveExecutablePath(bundlePath));
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(bundlePath)!, true);
            }
        }

        private static string CreateBundle(string bundleName, string plistContent)
        {
            string root = Path.Combine(Path.GetTempPath(), $"betterumm-bundle-{Guid.NewGuid():N}");
            string bundlePath = Path.Combine(root, bundleName);
            Directory.CreateDirectory(Path.Combine(bundlePath, "Contents"));
            File.WriteAllText(Path.Combine(bundlePath, "Contents", "Info.plist"), plistContent);
            return bundlePath;
        }
    }
}
