using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheWindowHider.Core;

/// <summary>
/// Persisted settings + rules. Stored as JSON under %AppData%\TheWindowHider\config.json.
/// Session-only (handle) rules are stripped before saving.
/// </summary>
public sealed class AppConfig
{
    public bool MasterEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;

    /// <summary>Whether the one-time "install to your programs folder?" prompt has been shown.</summary>
    public bool InstallPromptShown { get; set; }

    public List<HideRule> Rules { get; set; } = new();

    // ---- persistence ----

    [JsonIgnore]
    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TheWindowHider");

    [JsonIgnore]
    public static string FilePath => Path.Combine(Directory, "config.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                AppConfig? cfg = JsonSerializer.Deserialize<AppConfig>(json, Options);
                if (cfg != null)
                {
                    // Drop any stale session-only rules that shouldn't have been persisted.
                    cfg.Rules.RemoveAll(r => r.IsSessionOnly);
                    return cfg;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable config: fall back to defaults rather than crash.
        }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var toSave = new AppConfig
            {
                MasterEnabled = MasterEnabled,
                StartWithWindows = StartWithWindows,
                StartMinimized = StartMinimized,
                CloseToTray = CloseToTray,
                InstallPromptShown = InstallPromptShown,
                Rules = Rules.Where(r => !r.IsSessionOnly).ToList()
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(toSave, Options));
        }
        catch
        {
            // Best-effort; a failed save shouldn't take the app down.
        }
    }
}
