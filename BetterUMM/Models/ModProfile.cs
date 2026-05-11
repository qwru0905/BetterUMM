namespace BetterUMM.Models
{
    public class ModProfile
    {
        public string Name { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty; // e.g., Mods_Hardcore
        public bool IsActive { get; set; }
    }
}
