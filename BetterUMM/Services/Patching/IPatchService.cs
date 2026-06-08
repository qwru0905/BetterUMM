using BetterUMM.Models;

namespace BetterUMM.Services.Patching
{
    public enum PatchStatus
    {
        NotInstalled,
        AssemblyInjection,
        Doorstop
    }

    public interface IPatchService
    {
        PatchStatus GetPatchStatus(GameInfo game);
        bool InstallDoorstop(GameInfo game, string[] ummLibraryPaths);
        bool RemoveDoorstop(GameInfo game);
        bool InstallAssembly(GameInfo game, string[] ummLibraryPaths);
        bool RemoveAssembly(GameInfo game);
    }
}
