using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace TuneBar
{
    /// <summary>User settings, stored in %AppData%\TuneBar\settings.json.</summary>
    public sealed class Settings
    {
        // ---- general
        public bool StartWithWindows { get; set; } = true;
        public bool AnyPlayer { get; set; }

        // ---- position
        /// <summary>false = inside the taskbar (hides with it), true = float just above it.</summary>
        public bool AboveTaskbar { get; set; }
        /// <summary>Distance from the left edge of the taskbar, in DIPs.</summary>
        public double OffsetX { get; set; }
        public double WidgetWidth { get; set; } = 380;
        /// <summary>Fit into the gap between the Widgets button and Start (Windows 11, centered icons).</summary>
        public bool AutoFit { get; set; } = true;

        // ---- colors
        /// <summary>"Auto" (match Windows), "Dark", "Light" or "Custom".</summary>
        public string BackgroundMode { get; set; } = "Auto";
        public string BackgroundHex { get; set; } = "#202020";
        /// <summary>0 = see-through (blends into the taskbar), 1 = solid.</summary>
        public double BackgroundOpacity { get; set; } = 0.0;
        public string AccentHex { get; set; } = "#1DB954";
        public bool AccentFromArt { get; set; }

        // ---- runtime only
        [JsonIgnore] public bool IsFirstRun { get; private set; }

        /// <summary>Raised after any change is committed.</summary>
        public event Action? Changed;

        public static Settings Current { get; } = Load();

        private static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TuneBar");

        private static string FilePath => Path.Combine(Folder, "settings.json");

        private static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
            catch
            {
                // Corrupt file: fall back to defaults.
            }
            return new Settings { IsFirstRun = true };
        }

        /// <summary>Save to disk and tell everyone that something changed.</summary>
        public void Commit()
        {
            Save();
            Changed?.Invoke();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath,
                    JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Not fatal.
            }
        }

        public void ResetColors()
        {
            BackgroundMode = "Auto";
            BackgroundHex = "#202020";
            BackgroundOpacity = 0.0;
            AccentHex = "#1DB954";
            AccentFromArt = false;
        }
    }

    /// <summary>"Start with Windows" via the per-user Run key (no admin needed).</summary>
    internal static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "TuneBar";

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled && Environment.ProcessPath is string exe)
                    key.SetValue(ValueName, $"\"{exe}\"");
                else
                    key.DeleteValue(ValueName, false);
            }
            catch
            {
                // Ignore.
            }
        }
    }
}
