using WinPomTimer.Domain;

namespace WinPomTimer.Services;

public sealed class PomodoroTimerService : IDisposable
{
    private readonly System.Timers.Timer _timer;
    private PomodoroSettings _settings;
    private TimerMode _mode = TimerMode.Idle;
    private TimerMode _previousMode = TimerMode.Work;
    private TimeSpan _remaining = TimeSpan.Zero;
    private TimeSpan _duration = TimeSpan.Zero;
    private DateTimeOffset? _sessionStartAt;
    private bool _preAlertRaised;

    public int CycleCount { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }

    public event EventHandler<TimerSnapshot>? Tick;
    public event EventHandler<TimerSnapshot>? StateChanged;
    public event EventHandler<SessionLogEntry>? SessionCompleted;
    public event EventHandler<TimerMode>? SessionSwitched;
    public event EventHandler? PreBreakAlert;

    public PomodoroTimerService(PomodoroSettings settings)
    {
        _settings = settings;
        _timer = new System.Timers.Timer(1000);
        _timer.Elapsed += OnElapsed;
    }

    public TimerSnapshot Snapshot => new()
    {
        Mode = _mode,
        IsRunning = IsRunning,
        IsPaused = IsPaused,
        CycleCount = CycleCount,
        Remaining = _remaining,
        Duration = _duration
    };

    public void UpdateSettings(PomodoroSettings settings)
    {
        _settings = settings;
    }

    public void Start()
    {
        if (IsRunning && IsPaused)
        {
            return;
        }

        if (IsRunning)
        {
            return;
        }

        // Defensive recovery for persisted inconsistent state.
        if (IsPaused && !IsRunning)
        {
            IsPaused = false;
        }

        if (_mode == TimerMode.Idle)
        {
            SwitchTo(TimerMode.Work, incrementCycle: false);
        }

        IsRunning = true;
        IsPaused = false;
        _timer.Start();
        EmitState();
    }

    public void Pause()
    {
        if (!IsRunning || IsPaused || _mode == TimerMode.Idle)
        {
            return;
        }

        IsPaused = true;
        _previousMode = _mode;
        _timer.Stop();
        EmitState();
    }

    public void Resume()
    {
        if (!IsRunning || !IsPaused)
        {
            return;
        }

        IsPaused = false;
        _timer.Start();
        EmitState();
    }

    public void Skip()
    {
        if (_mode == TimerMode.Idle && !IsRunning)
        {
            return;
        }

        CompleteCurrentSession(completed: false);
        MoveNextSession();
        EmitState();
    }

    public void Reset()
    {
        _timer.Stop();
        IsRunning = false;
        IsPaused = false;
        _mode = TimerMode.Idle;
        _previousMode = TimerMode.Work;
        _remaining = TimeSpan.Zero;
        _duration = TimeSpan.Zero;
        _sessionStartAt = null;
        _preAlertRaised = false;
        EmitState();
    }

    public void Restore(AppRuntimeState state)
    {
        _mode = state.Mode;
        _previousMode = state.PreviousMode;
        IsRunning = state.IsRunning;
        IsPaused = state.IsPaused;
        CycleCount = state.CycleCount;
        _remaining = TimeSpan.FromSeconds(state.RemainingSeconds);
        _duration = TimeSpan.FromSeconds(state.DurationSeconds);
        _preAlertRaised = false;

        // Sanitize inconsistent persisted state to avoid dead "can't start" UI.
        if (!IsRunning && IsPaused)
        {
            IsPaused = false;
        }

        if (IsRunning && !IsPaused && _mode != TimerMode.Idle)
        {
            _sessionStartAt = DateTimeOffset.Now - (_duration - _remaining);
            _timer.Start();
        }

        EmitState();
    }

    public AppRuntimeState ToRuntimeState()
    {
        return new AppRuntimeState
        {
            Mode = _mode,
            PreviousMode = _previousMode,
            IsRunning = IsRunning,
            IsPaused = IsPaused,
            CycleCount = CycleCount,
            RemainingSeconds = (int)Math.Max(0, _remaining.TotalSeconds),
            DurationSeconds = (int)Math.Max(0, _duration.TotalSeconds),
            SavedAt = DateTimeOffset.Now
        };
    }

    private void OnElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (!IsRunning || IsPaused || _mode == TimerMode.Idle)
        {
            return;
        }

        _remaining -= TimeSpan.FromSeconds(1);
        if (_remaining < TimeSpan.Zero)
        {
            _remaining = TimeSpan.Zero;
        }

        var preAlertSeconds = Math.Max(0, _settings.PreBreakAlertSeconds);
        if (!_preAlertRaised && preAlertSeconds > 0 && IsBreakMode(_mode) && _remaining.TotalSeconds <= preAlertSeconds)
        {
            _preAlertRaised = true;
            PreBreakAlert?.Invoke(this, EventArgs.Empty);
        }

        Tick?.Invoke(this, Snapshot);

        if (_remaining == TimeSpan.Zero)
        {
            CompleteCurrentSession(completed: true);
            MoveNextSession();
            EmitState();
        }
    }

    private void CompleteCurrentSession(bool completed)
    {
        if (_sessionStartAt is null || _duration == TimeSpan.Zero)
        {
            return;
        }

        SessionCompleted?.Invoke(this, new SessionLogEntry
        {
            StartAt = _sessionStartAt.Value,
            EndAt = DateTimeOffset.Now,
            Mode = _mode,
            Completed = completed
        });
    }

    private void MoveNextSession()
    {
        if (_mode == TimerMode.Work)
        {
            if ((CycleCount + 1) % Math.Max(1, _settings.LongBreakInterval) == 0)
            {
                SwitchTo(TimerMode.LongBreak, incrementCycle: true);
            }
            else
            {
                SwitchTo(TimerMode.ShortBreak, incrementCycle: true);
            }

            if (!_settings.AutoStartBreak)
            {
                IsRunning = false;
                _timer.Stop();
            }
            return;
        }

        if (IsBreakMode(_mode))
        {
            SwitchTo(TimerMode.Work, incrementCycle: false);
            if (!_settings.AutoStartWork)
            {
                IsRunning = false;
                _timer.Stop();
            }
        }
    }

    private void SwitchTo(TimerMode next, bool incrementCycle)
    {
        if (incrementCycle && _mode == TimerMode.Work)
        {
            CycleCount++;
        }

        _mode = next;
        _previousMode = next;
        _duration = GetDuration(next);
        _remaining = _duration;
        _sessionStartAt = DateTimeOffset.Now;
        _preAlertRaised = false;
        SessionSwitched?.Invoke(this, next);
    }

    private TimeSpan GetDuration(TimerMode mode)
    {
        return mode switch
        {
            TimerMode.Work => TimeSpan.FromMinutes(Math.Max(1, _settings.WorkMinutes)),
            TimerMode.ShortBreak => TimeSpan.FromMinutes(Math.Max(1, _settings.ShortBreakMinutes)),
            TimerMode.LongBreak => TimeSpan.FromMinutes(Math.Max(1, _settings.LongBreakMinutes)),
            _ => TimeSpan.Zero
        };
    }

    private static bool IsBreakMode(TimerMode mode)
    {
        return mode == TimerMode.ShortBreak || mode == TimerMode.LongBreak;
    }

    private void EmitState()
    {
        StateChanged?.Invoke(this, Snapshot);
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
