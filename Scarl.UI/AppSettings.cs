using System;
using System.IO;
using System.Text.Json;

namespace Scarl.UI
{
    public class AppSettings
    {
        public string? DefaultSaveFolder { get; set; }
        public string Theme { get; set; } = "Red"; // Red, Blue, Gold
        public double GlassIntensity { get; set; } = 80; // 0 to 100
        public double WindowWidth { get; set; } = 1100;
        public double WindowHeight { get; set; } = 800;

        private static readonly string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }
}
