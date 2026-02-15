namespace WinPomTimer.Domain;

public sealed class SessionLogEntry
{
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public TimerMode Mode { get; set; }
    public bool Completed { get; set; }
    public string Note { get; set; } = string.Empty;
    public List<string> TagIds { get; set; } = new();
}
