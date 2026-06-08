using BetterUMM.Services.Patching;
using Xunit;

namespace BetterUMM.Tests.Services.Patching
{
    public class PatchServiceFactoryTests
    {
        [Fact]
        public void Create_ReturnsNonNullPlatformAppropriateService()
        {
            IPatchService service = PatchServiceFactory.Create();

            Assert.NotNull(service);
            if (System.OperatingSystem.IsWindows())
                Assert.IsType<WindowsPatchService>(service);
            else
                Assert.IsType<UnixDoorstopPatchService>(service);
        }
    }
}
