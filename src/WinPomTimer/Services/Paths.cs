using System.IO;

namespace WinPomTimer.Services;

public static class Paths
{
    public static string AppDataDir
    {
        get
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WinPomTimer");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SettingsFile => System.IO.Path.Combine(AppDataDir, "settings.json");
    public static string StateFile => System.IO.Path.Combine(AppDataDir, "state.json");
    public static string SessionsFile => System.IO.Path.Combine(AppDataDir, "sessions.jsonl");
}
