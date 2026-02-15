using System.IO;
using System.Text;
using System.Text.Json;
using WinPomTimer.Domain;

namespace WinPomTimer.Services;

public sealed class SessionLogService
{
    public void Append(SessionLogEntry entry)
    {
        var json = JsonSerializer.Serialize(entry);
        File.AppendAllText(Paths.SessionsFile, json + Environment.NewLine, Encoding.UTF8);
    }

    public IReadOnlyList<SessionLogEntry> ReadAll()
    {
        if (!File.Exists(Paths.SessionsFile))
        {
            return Array.Empty<SessionLogEntry>();
        }

        var items = new List<SessionLogEntry>();
        foreach (var line in File.ReadLines(Paths.SessionsFile))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<SessionLogEntry>(line);
                if (parsed is not null)
                {
                    parsed.TagIds ??= new List<string>();
                    items.Add(parsed);
                }
            }
            catch
            {
                // skip invalid line
            }
        }

        return items;
    }
}
