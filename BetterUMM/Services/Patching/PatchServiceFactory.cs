using System;
using System.Runtime.InteropServices;

namespace BetterUMM.Services.Patching
{
    public static class PatchServiceFactory
    {
        public static IPatchService Create()
        {
            if (OperatingSystem.IsWindows()) return new WindowsPatchService();
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) return new UnixDoorstopPatchService();
            throw new PlatformNotSupportedException($"지원하지 않는 운영체제입니다: {RuntimeInformation.OSDescription}");
        }
    }
}
