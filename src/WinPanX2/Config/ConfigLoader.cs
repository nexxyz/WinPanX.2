using System.Text.Json;

namespace WinPanX2.Config;

internal static class ConfigLoader
{
    public static AppConfig LoadOrCreate(string path)
    {
        EnsureParentDirectory(path);

        if (!File.Exists(path))
        {
            var defaultConfig = new AppConfig();
            defaultConfig.Normalize();
            Save(path, defaultConfig);
            return defaultConfig;
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<AppConfig>(json);
            var resolved = config ?? new AppConfig();
            resolved.Normalize();
            return resolved;
        }
        catch
        {
            var fallback = new AppConfig();
            fallback.Normalize();
            Save(path, fallback);
            return fallback;
        }
    }

    public static void Save(string path, AppConfig config)
    {
        EnsureParentDirectory(path);
        config.Normalize();

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(path, json);
    }

    private static void EnsureParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);
    }
}
