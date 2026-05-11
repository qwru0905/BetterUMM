using System;
using System.IO;
using BetterUMM.Models;
using Newtonsoft.Json;

namespace BetterUMM.Services
{
    public class AppSettingsService
    {
        private static readonly string SettingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        private AppSettings _settings = new();

        public AppSettings Settings => _settings;

        public AppSettingsService()
        {
            LoadSettings();
        }

        public void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    _settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                }
                else
                {
                    _settings = new AppSettings();
                    SaveSettings(); // Create default file
                }

                LoggerService.MinimumLevel = _settings.LogLevel;
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "LoadAppSettings");
            }
        }

        public void SaveSettings()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json);
                
                LoggerService.MinimumLevel = _settings.LogLevel;
                LoggerService.Info($"AppSettings saved. LogLevel: {_settings.LogLevel}");
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "SaveAppSettings");
            }
        }
    }
}
