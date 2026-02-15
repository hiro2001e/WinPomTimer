namespace WinPomTimer.Domain;

public sealed class TimerSnapshot
{
    public TimerMode Mode { get; init; } = TimerMode.Idle;
    public bool IsRunning { get; init; }
    public bool IsPaused { get; init; }
    public int CycleCount { get; init; }
    public TimeSpan Remaining { get; init; } = TimeSpan.Zero;
    public TimeSpan Duration { get; init; } = TimeSpan.Zero;
}

