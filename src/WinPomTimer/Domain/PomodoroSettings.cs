using System.IO;

namespace WinPomTimer.Domain;

public sealed class PomodoroSettings
{
    public const string DefaultTickSoundFileName = "\u79D2\u91DD.wav";
    public const string DefaultWorkEndSoundFileName = "\u9CE9\u6642\u8A081.wav";
    public static string PreferredTickSoundPath => Path.Combine(AppContext.BaseDirectory, DefaultTickSoundFileName);

    public int WorkMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int LongBreakInterval { get; set; } = 4;
    public bool AutoStartBreak { get; set; } = true;
    public bool AutoStartWork { get; set; } = false;

    public bool MuteAll { get; set; }
    public int MasterVolumePercent { get; set; } = 80;
    public int PreBreakAlertSeconds { get; set; } = 10;

    public bool TickSoundEnabled { get; set; } = false;
    public int TickVolumePercent { get; set; } = 30;
    public TickSoundMode TickSoundMode { get; set; } = TickSoundMode.WorkOnly;
    public bool EnableSystemNotifications { get; set; } = true;

    public string? SessionSwitchSoundPath { get; set; }
    public string? WorkEndSoundPath { get; set; }
    public string? WorkStartSoundPath { get; set; }
    public string? PreBreakAlertSoundPath { get; set; }
    public string? TickSoundPath { get; set; }

    public bool AlwaysOnTop { get; set; } = false;
    public bool EnableMouseLeaveOpacity { get; set; } = true;
    public double MouseLeaveOpacity { get; set; } = 0.55;
    public string ClockFontFamily { get; set; } = "Consolas";
    public string ShellBackgroundColorHex { get; set; } = "#121826";
    public double ShellBackgroundOpacity { get; set; } = 0.87;
    public string MeterColorHex { get; set; } = "#F28A4B";
    public string ClockTextColorHex { get; set; } = "#FFFFFF";
    public string ModeTextColorHex { get; set; } = "#D8E9FF";
    public List<TaskTag> Tags { get; set; } = new();

    public PomodoroSettings Clone()
    {
        var clone = (PomodoroSettings)MemberwiseClone();
        clone.Tags = Tags
            .Select(x => x.Clone())
            .ToList();
        return clone;
    }
}

public enum TickSoundMode
{
    WorkOnly = 0,
    AllSessions = 1
}
