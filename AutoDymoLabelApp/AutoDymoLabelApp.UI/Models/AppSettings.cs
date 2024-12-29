using System;
using System.IO;
using System.Text.Json;

namespace AutoDymoLabelApp.UI.Models
{
    public class AppSettings
    {
        public bool AutoActivate { get; set; } = false;
        public string SelectedDeviceKey { get; set; } = string.Empty;
        public bool Enable85PercentChecker { get; set; } = false;
        public bool UseDymoAPI { get; set; } = false;
        public bool EnableDataEditor { get; set; } = true;

        private static readonly string SettingsFilePath = "settings.json";

        public static AppSettings Load()
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }

        public void Save()
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
    }
}