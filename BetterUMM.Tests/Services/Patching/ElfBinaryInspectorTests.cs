using System;
using System.IO;
using BetterUMM.Services.Patching;
using Xunit;

namespace BetterUMM.Tests.Services.Patching
{
    public class ElfBinaryInspectorTests
    {
        [Fact]
        public void Is64Bit_ForElf64Header_ReturnsTrue()
        {
            string path = WriteTempFile(new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 0x02, 0x01, 0x01, 0x00, 0x00 });
            try
            {
                Assert.Equal(true, ElfBinaryInspector.Is64Bit(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Is64Bit_ForElf32Header_ReturnsFalse()
        {
            string path = WriteTempFile(new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 0x01, 0x01, 0x01, 0x00, 0x00 });
            try
            {
                Assert.Equal(false, ElfBinaryInspector.Is64Bit(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Is64Bit_ForNonElfHeader_ReturnsNull()
        {
            string path = WriteTempFile(new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00 });
            try
            {
                Assert.Null(ElfBinaryInspector.Is64Bit(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Is64Bit_ForMissingFile_ReturnsNull()
        {
            string path = Path.Combine(Path.GetTempPath(), $"betterumm-elf-missing-{Guid.NewGuid():N}.bin");
            Assert.Null(ElfBinaryInspector.Is64Bit(path));
        }

        private static string WriteTempFile(byte[] content)
        {
            string path = Path.Combine(Path.GetTempPath(), $"betterumm-elf-{Guid.NewGuid():N}.bin");
            File.WriteAllBytes(path, content);
            return path;
        }
    }
}
