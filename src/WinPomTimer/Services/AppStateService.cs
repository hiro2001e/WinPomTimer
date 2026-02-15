using System.IO;
using System.Text.Json;
using WinPomTimer.Domain;

namespace WinPomTimer.Services;

public sealed class AppStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppRuntimeState? Load()
    {
        try
        {
            if (!File.Exists(Paths.StateFile))
            {
                return null;
            }

            var json = File.ReadAllText(Paths.StateFile);
            return JsonSerializer.Deserialize<AppRuntimeState>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(AppRuntimeState state)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(Paths.StateFile, json);
    }
}
