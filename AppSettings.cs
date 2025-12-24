// AppSettings.cs
//===========================
// Manages persistent application settings including the app data folder path

using System;
using System.IO;
using Windows.Storage;

namespace GolfApp1
{
    internal static class AppSettings
    {
        private const string SETTINGS_FILE = "appsettings.txt";
        private const string FOLDER_PATH_KEY = "AppDataFolder";

        /// <summary>
        /// Gets the currently configured app folder path.
        /// Returns null if no path is configured.
        /// </summary>
        public static string? GetAppDataFolderPath()
        {
            try
            {
                var settingsPath = GetSettingsFilePath();
                if (!File.Exists(settingsPath))
                    return null;

                var lines = File.ReadAllLines(settingsPath);
                foreach (var line in lines)
                {
                    if (line.StartsWith(FOLDER_PATH_KEY + "="))
                    {
                        var path = line.Substring(FOLDER_PATH_KEY.Length + 1).Trim();
                        return string.IsNullOrWhiteSpace(path) ? null : path;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Saves the app folder path to persistent settings.
        /// Creates the settings file if it doesn't exist.
        /// </summary>
        public static bool SetAppDataFolderPath(string folderPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath))
                    return false;

                var settingsPath = GetSettingsFilePath();
                var settingsDir = Path.GetDirectoryName(settingsPath);
                if (!string.IsNullOrEmpty(settingsDir) && !Directory.Exists(settingsDir))
                {
                    Directory.CreateDirectory(settingsDir);
                }

                // Write settings file
                var content = $"{FOLDER_PATH_KEY}={folderPath}\n";
                File.WriteAllText(settingsPath, content);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Clears the app folder path setting.
        /// </summary>
        public static bool ClearAppDataFolderPath()
        {
            try
            {
                var settingsPath = GetSettingsFilePath();
                if (File.Exists(settingsPath))
                {
                    File.Delete(settingsPath);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if an app folder path is currently configured.
        /// </summary>
        public static bool HasConfiguredFolder()
        {
            var path = GetAppDataFolderPath();
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        }

        /// <summary>
        /// Gets the full path to the settings file.
        /// Stores in LocalFolder for packaged apps, or AppData for unpackaged.
        /// </summary>
        private static string GetSettingsFilePath()
        {
            try
            {
                var appData = ApplicationData.Current;
                var localFolder = appData?.LocalFolder?.Path;
                if (!string.IsNullOrWhiteSpace(localFolder))
                {
                    return Path.Combine(localFolder, SETTINGS_FILE);
                }
            }
            catch { /* Unpackaged scenario */ }

            // Fallback for unpackaged apps
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(localAppData, "GolfApp1");
            return Path.Combine(appFolder, SETTINGS_FILE);
        }

        /// <summary>
        /// Gets the current database file location (for migration purposes).
        /// Returns null if not found.
        /// </summary>
        public static string? GetCurrentDatabaseLocation()
        {
            try
            {
                // Check configured folder first
                var configuredPath = GetAppDataFolderPath();
                if (!string.IsNullOrWhiteSpace(configuredPath))
                {
                    var dbPath = Path.Combine(configuredPath, "golfapp.db");
                    if (File.Exists(dbPath))
                        return dbPath;
                }

                // Check LocalFolder (WinUI packaged app default)
                try
                {
                    var appData = ApplicationData.Current;
                    var localFolder = appData?.LocalFolder?.Path;
                    if (!string.IsNullOrWhiteSpace(localFolder))
                    {
                        var dbPath = Path.Combine(localFolder, "golfapp.db");
                        if (File.Exists(dbPath))
                            return dbPath;
                    }
                }
                catch { /* ignore */ }

                // Check unpackaged fallback location
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appFolder = Path.Combine(localAppData, "GolfApp1");
                var fallbackDb = Path.Combine(appFolder, "golfapp.db");
                if (File.Exists(fallbackDb))
                    return fallbackDb;

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
