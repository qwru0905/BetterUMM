namespace BetterUMM.Models
{
    public class GameInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty; // Path to the executable
        public string GameDataPath { get; set; } = string.Empty; // Path to *_Data folder
        public string AssemblyName { get; set; } = "Assembly-CSharp.dll";
        public string PatchTarget { get; set; } = string.Empty;
        public PatchMethod CurrentPatchMethod { get; set; } = PatchMethod.None;
        public string UMMVersion { get; set; } = string.Empty;

        // UMM Config fields
        public string Folder { get; set; } = string.Empty;
        public string ModsDirectory { get; set; } = "Mods";
        public string ModInfo { get; set; } = "Info.json";
        public string GameExe { get; set; } = string.Empty;
        public string EntryPoint { get; set; } = string.Empty;
        public string StartingPoint { get; set; } = string.Empty;
        public string UIStartingPoint { get; set; } = string.Empty;
        public string OldPatchTarget { get; set; } = string.Empty;
        public string GameVersionPoint { get; set; } = string.Empty;
        public string MinimalManagerVersion { get; set; } = string.Empty;
        public string HarmonyVersion { get; set; } = string.Empty;
    }

    public enum PatchMethod
    {
        None,
        Assembly,
        Doorstop
    }
}
