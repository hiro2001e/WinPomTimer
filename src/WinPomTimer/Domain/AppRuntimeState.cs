namespace WinPomTimer.Domain;

public sealed class AppRuntimeState
{
    public TimerMode Mode { get; set; } = TimerMode.Idle;
    public TimerMode PreviousMode { get; set; } = TimerMode.Work;
    public bool IsRunning { get; set; }
    public bool IsPaused { get; set; }
    public int CycleCount { get; set; }
    public int RemainingSeconds { get; set; }
    public int DurationSeconds { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;
}

