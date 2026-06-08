using BetterUMM.Models;

namespace BetterUMM.Services.Patching
{
    public class WindowsPatchService : IPatchService
    {
        public PatchStatus GetPatchStatus(GameInfo game) => throw new System.NotImplementedException();
        public bool InstallDoorstop(GameInfo game, string[] ummLibraryPaths) => throw new System.NotImplementedException();
        public bool RemoveDoorstop(GameInfo game) => throw new System.NotImplementedException();
        public bool InstallAssembly(GameInfo game, string[] ummLibraryPaths) => throw new System.NotImplementedException();
        public bool RemoveAssembly(GameInfo game) => throw new System.NotImplementedException();
    }
}
