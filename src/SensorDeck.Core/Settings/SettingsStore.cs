using System.Text.Json;
using System.Text.Json.Serialization;

namespace SensorDeck.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON. Missing or corrupt files fall back to defaults
/// (a corrupt file is kept as settings.json.bad for inspection). Saves are atomic (write temp, then replace).
/// </summary>
public sealed class SettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string FilePath { get; } = filePath;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SensorDeck", "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json);
            return (loaded ?? new AppSettings()).Normalize();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            try { File.Copy(FilePath, FilePath + ".bad", overwrite: true); } catch { /* best effort */ }
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
