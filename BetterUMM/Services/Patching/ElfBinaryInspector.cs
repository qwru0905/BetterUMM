using System;
using System.IO;
using BetterUMM.Services;

namespace BetterUMM.Services.Patching
{
    public static class ElfBinaryInspector
    {
        public static bool? Is64Bit(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                Span<byte> header = stackalloc byte[5];
                if (stream.Read(header) != header.Length) return null;
                if (header[0] != 0x7F || header[1] != (byte)'E' || header[2] != (byte)'L' || header[3] != (byte)'F')
                    return null;
                return header[4] switch
                {
                    1 => false,
                    2 => true,
                    _ => null
                };
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, $"ElfBinaryInspector.Is64Bit: {filePath}");
                return null;
            }
        }
    }
}
