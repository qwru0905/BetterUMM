using System.IO;
using BetterUMM.Models;

namespace BetterUMM.Services
{
    public class ProfileService
    {
        public void SwitchProfile(string gameRootPath, ModProfile current, ModProfile target)
        {
            string modsPath = Path.Combine(gameRootPath, "Mods");
            
            // 1. Rename current Mods to Mods_ProfileName
            if (Directory.Exists(modsPath))
            {
                string backupPath = Path.Combine(gameRootPath, current.FolderName);
                if (!Directory.Exists(backupPath))
                {
                    Directory.Move(modsPath, backupPath);
                }
            }

            // 2. Rename target Mods_ProfileName to Mods
            string targetPath = Path.Combine(gameRootPath, target.FolderName);
            if (Directory.Exists(targetPath))
            {
                Directory.Move(targetPath, modsPath);
            }
            else
            {
                // If target doesn't exist, create an empty Mods folder
                Directory.CreateDirectory(modsPath);
            }
        }
    }
}
