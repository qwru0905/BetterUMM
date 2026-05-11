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
    }

    public enum PatchMethod
    {
        None,
        Assembly,
        Doorstop
    }
}
