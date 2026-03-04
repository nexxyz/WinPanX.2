using System.Text.Json;

namespace WinPanX2.Config;

internal static class ConfigLoader
{
    public static AppConfig LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            var defaultConfig = new AppConfig();
            Save(path, defaultConfig);
            return defaultConfig;
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<AppConfig>(json);
            return config ?? new AppConfig();
        }
        catch
        {
            var fallback = new AppConfig();
            Save(path, fallback);
            return fallback;
        }
    }

    private static void Save(string path, AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(path, json);
    }
}
