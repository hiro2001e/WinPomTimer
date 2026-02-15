using System.IO;
using System.Text.Json;
using WinPomTimer.Domain;

namespace WinPomTimer.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public PomodoroSettings Load()
    {
        try
        {
            if (!File.Exists(Paths.SettingsFile))
            {
                var defaults = new PomodoroSettings();
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(Paths.SettingsFile);
            var loaded = JsonSerializer.Deserialize<PomodoroSettings>(json, JsonOptions);
            var settings = loaded ?? new PomodoroSettings();
            settings.Tags ??= new List<TaskTag>();
            NormalizeTagSettings(settings.Tags);
            if (ApplyTickPathMigration(settings))
            {
                Save(settings);
            }

            return settings;
        }
        catch
        {
            return new PomodoroSettings();
        }
    }

    public void Save(PomodoroSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(Paths.SettingsFile, json);
    }

    private static bool ApplyTickPathMigration(PomodoroSettings settings)
    {
        var changed = false;
        var preferred = PomodoroSettings.PreferredTickSoundPath;
        var legacyGenerated = Path.Combine(Paths.AppDataDir, "sounds", "tick_default.wav");
        var currentPath = settings.TickSoundPath?.Trim();

        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return false;
        }

        // Legacy generated tick sound path from older versions.
        if (string.Equals(currentPath, legacyGenerated, StringComparison.OrdinalIgnoreCase))
        {
            settings.TickSoundPath = File.Exists(preferred) ? preferred : null;
            return true;
        }

        // Legacy absolute default path from development environment.
        if (Path.IsPathRooted(currentPath) &&
            !File.Exists(currentPath) &&
            string.Equals(Path.GetFileName(currentPath), PomodoroSettings.DefaultTickSoundFileName, StringComparison.OrdinalIgnoreCase))
        {
            settings.TickSoundPath = File.Exists(preferred) ? preferred : null;
            changed = true;
        }

        return changed;
    }

    private static void NormalizeTagSettings(List<TaskTag> tags)
    {
        for (var i = tags.Count - 1; i >= 0; i--)
        {
            var tag = tags[i];
            if (tag is null || string.IsNullOrWhiteSpace(tag.Name))
            {
                tags.RemoveAt(i);
                continue;
            }

            if (string.IsNullOrWhiteSpace(tag.Id))
            {
                tag.Id = Guid.NewGuid().ToString("N");
            }

            if (string.IsNullOrWhiteSpace(tag.ColorHex))
            {
                tag.ColorHex = "#4B8BF4";
            }

            if (!Enum.IsDefined(tag.Axis))
            {
                tag.Axis = TagAxis.WorkType;
            }

            tag.Name = tag.Name.Trim();
            tag.ColorHex = tag.ColorHex.Trim();
        }
    }
}
