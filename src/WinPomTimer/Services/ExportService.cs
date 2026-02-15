using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using WinPomTimer.Domain;

namespace WinPomTimer.Services;

public sealed class ExportService
{
    public void ExportJson(IEnumerable<SessionLogEntry> sessions, string destinationPath)
    {
        var json = JsonSerializer.Serialize(sessions, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(destinationPath, json, Encoding.UTF8);
    }

    public void ExportCsv(IEnumerable<SessionLogEntry> sessions, string destinationPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("start_at,end_at,mode,completed,note");
        foreach (var s in sessions)
        {
            var note = EscapeCsv(s.Note);
            sb.AppendLine($"{s.StartAt:O},{s.EndAt:O},{s.Mode},{s.Completed},{note}");
        }

        File.WriteAllText(destinationPath, sb.ToString(), Encoding.UTF8);
    }

    public void ExportIcs(IEnumerable<SessionLogEntry> sessions, string destinationPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine("PRODID:-//WinPomTimer//EN");

        foreach (var s in sessions)
        {
            var uid = $"{Guid.NewGuid()}@winpomt";
            var start = s.StartAt.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            var end = s.EndAt.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            var summary = EscapeIcsText(s.Mode == TimerMode.Work ? "Pomodoro Work" : "Pomodoro Break");
            var description = EscapeIcsText(string.IsNullOrWhiteSpace(s.Note) ? "Logged by WinPomTimer" : s.Note);

            sb.AppendLine("BEGIN:VEVENT");
            sb.AppendLine($"UID:{uid}");
            sb.AppendLine($"DTSTAMP:{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}");
            sb.AppendLine($"DTSTART:{start}");
            sb.AppendLine($"DTEND:{end}");
            sb.AppendLine($"SUMMARY:{summary}");
            sb.AppendLine($"DESCRIPTION:{description}");
            sb.AppendLine("END:VEVENT");
        }

        sb.AppendLine("END:VCALENDAR");
        File.WriteAllText(destinationPath, sb.ToString(), Encoding.UTF8);
    }

    private static string EscapeCsv(string input)
    {
        var safe = NeutralizeCsvFormula(input ?? string.Empty);
        var normalized = safe.Replace("\"", "\"\"");
        return $"\"{normalized}\"";
    }

    private static string NeutralizeCsvFormula(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var firstMeaningfulIndex = 0;
        while (firstMeaningfulIndex < input.Length && char.IsWhiteSpace(input[firstMeaningfulIndex]))
        {
            firstMeaningfulIndex++;
        }

        if (firstMeaningfulIndex >= input.Length)
        {
            return input;
        }

        var first = input[firstMeaningfulIndex];
        if (first is '=' or '+' or '-' or '@' || first == '\t' || first == '\r')
        {
            return "'" + input;
        }

        return input;
    }

    private static string EscapeIcsText(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var normalized = input
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

        // RFC5545 text escaping: backslash, semicolon, comma, newline
        return normalized
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\n", "\\n");
    }
}
