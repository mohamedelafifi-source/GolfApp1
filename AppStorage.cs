using System;
using System.IO;
using Windows.Storage;

namespace GolfApp1
{
    internal static class AppStorage
    {
        public static string GetDataFolder()
        {
            try
            {
                var appData = ApplicationData.Current;
                var path = appData?.LocalFolder?.Path;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    return path;
                }
            }
            catch
            {
                // Activation may fail in unpackaged scenarios — fall back.
            }

            var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GolfApp1");
            try { Directory.CreateDirectory(fallback); } catch { /* ignore */ }
            return fallback;
        }
    }
}
