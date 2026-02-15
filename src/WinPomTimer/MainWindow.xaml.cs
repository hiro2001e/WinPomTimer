using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinPomTimer.Domain;
using WinPomTimer.Services;
using Forms = System.Windows.Forms;

namespace WinPomTimer;

public partial class MainWindow : Window
{
    private const string DefaultHint = "1ボタンで 開始 / 一時停止 / 再開 を切り替え";
    private const string PendingMemoHint = "作業終了。メモ入力後に「休憩開始」を押してください。";
    private const double GhostWidth = 260;
    private const double GhostHeight = 260;
    private const double MinVisiblePixels = 80;
    private const double MeterCenter = 86;
    private const double MeterRadius = 82;

    private readonly SettingsService _settingsService = new();
    private readonly AppStateService _appStateService = new();
    private readonly SessionLogService _sessionLogService = new();
    private readonly ExportService _exportService = new();
    private readonly StatsService _statsService = new();

    private PomodoroSettings _settings;
    private readonly PomodoroTimerService _timerService;
    private readonly AudioService _audioService;
    private readonly DispatcherTimer _stateSaveTimer;
    private readonly DispatcherTimer _opacityTimer;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly HashSet<string> _selectedTagIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, System.Windows.Media.Color> _tagColorMap = new(StringComparer.OrdinalIgnoreCase);

    private SessionLogEntry? _pendingWorkMemoEntry;
    private bool _allowExit;
    private bool _isGhostMode;
    private bool _suspendBoundsCapture;
    private double _normalWidth = 420;
    private double _normalHeight = 640;
    private double _normalLeft;
    private double _normalTop;

    public MainWindow()
    {
        InitializeComponent();

        _normalLeft = Left;
        _normalTop = Top;
        StateHintText.Text = DefaultHint;

        _settings = _settingsService.Load();
        _timerService = new PomodoroTimerService(_settings);
        _audioService = new AudioService(_settings);
        _stateSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _stateSaveTimer.Tick += (_, _) => SaveRuntimeState();
        _opacityTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _opacityTimer.Tick += (_, _) => UpdateGhostMode();

        _timerService.Tick += OnTimerTick;
        _timerService.StateChanged += OnStateChanged;
        _timerService.SessionSwitched += OnSessionSwitched;
        _timerService.SessionCompleted += OnSessionCompleted;
        _timerService.PreBreakAlert += OnPreBreakAlert;

        _notifyIcon = BuildNotifyIcon();

        ApplyRuntimeSettings(_settings);
        RebuildTagSelector();
        RestoreRuntimeStateIfAvailable();
        RefreshProgress();
        RenderSnapshot(_timerService.Snapshot);
        EnsureWindowVisible(allowPartial: true);
        UpdateMemoInputUi();

        _stateSaveTimer.Start();
        _opacityTimer.Start();

        Closing += MainWindow_Closing;
        MouseLeave += (_, _) => UpdateGhostMode();
        MouseEnter += (_, _) => UpdateGhostMode();
        Activated += (_, _) => UpdateGhostMode();
        Deactivated += (_, _) => UpdateGhostMode();
        SizeChanged += (_, _) => CaptureNormalBounds();
        LocationChanged += (_, _) => CaptureNormalBounds();
    }

    private Forms.NotifyIcon BuildNotifyIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("表示", null, (_, _) => ShowFromTray());
        menu.Items.Add("開始/一時停止/再開", null, (_, _) => PrimaryAction());
        menu.Items.Add("次へ", null, (_, _) => _timerService.Skip());
        menu.Items.Add("中央に戻す", null, (_, _) => Dispatcher.Invoke(CenterWindowOnScreen));
        menu.Items.Add("設定", null, (_, _) => Dispatcher.Invoke(OpenSettingsDialog));
        menu.Items.Add("終了", null, (_, _) => ExitApplication());

        var icon = new Forms.NotifyIcon
        {
            Icon = LoadNotifyIcon(),
            Visible = true,
            Text = "ポモドーロタイマー",
            ContextMenuStrip = menu
        };

        icon.DoubleClick += (_, _) => ShowFromTray();
        return icon;
    }

    private static System.Drawing.Icon LoadNotifyIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "WinPomTimer.ico");
        try
        {
            if (File.Exists(iconPath))
            {
                return new System.Drawing.Icon(iconPath);
            }
        }
        catch
        {
            // Fall back to system icon when custom icon is unavailable.
        }

        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }

    private void OnTimerTick(object? sender, TimerSnapshot snapshot)
    {
        Dispatcher.Invoke(() =>
        {
            RenderSnapshot(snapshot);
            _audioService.PlayTick(snapshot.Mode);
        });
    }

    private void OnStateChanged(object? sender, TimerSnapshot snapshot)
    {
        Dispatcher.Invoke(() => RenderSnapshot(snapshot));
    }

    private void OnSessionSwitched(object? sender, TimerMode mode)
    {
        Dispatcher.Invoke(() =>
        {
            if (mode == TimerMode.Work)
            {
                ClearSelectedTags();
                _audioService.PlaySessionSwitch();
                _audioService.PlayWorkStart();
            }

            ShowBalloon("セッション切替", $"現在: {GetModeLabel(mode, false)}", 1400);
        });
    }

    private void OnSessionCompleted(object? sender, SessionLogEntry entry)
    {
        Dispatcher.Invoke(() =>
        {
            if (entry.Mode == TimerMode.Work && entry.Completed)
            {
                _audioService.PlayWorkEnd();
                _pendingWorkMemoEntry = entry;
                _pendingWorkMemoEntry.TagIds = _selectedTagIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                UpdateMemoInputUi();

                var snap = _timerService.Snapshot;
                if (snap.IsRunning && !snap.IsPaused)
                {
                    _timerService.Pause();
                }

                RenderSnapshot(_timerService.Snapshot);
                return;
            }

            if (entry.Mode == TimerMode.Work)
            {
                entry.Note = NoteTextBox.Text.Trim();
                entry.TagIds = _selectedTagIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                NoteTextBox.Text = string.Empty;
            }

            _sessionLogService.Append(entry);
            RefreshProgress();
            UpdateMemoInputUi();
        });
    }

    private void OnPreBreakAlert(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(_audioService.PlayPreBreakAlert);
    }

    private void RenderSnapshot(TimerSnapshot snapshot)
    {
        ModeText.Text = GetModeLabel(snapshot.Mode, snapshot.IsPaused);
        RemainingText.Text = snapshot.Remaining.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        _notifyIcon.Text = $"ポモドーロ {ModeText.Text} {RemainingText.Text}";
        UpdateMeter(snapshot);
        UpdatePrimaryActionButton(snapshot);
        UpdateMemoInputUi();
        SkipButton.IsEnabled = snapshot.IsRunning || snapshot.Mode != TimerMode.Idle;
        ResetButton.IsEnabled = snapshot.IsRunning || snapshot.Mode != TimerMode.Idle || snapshot.CycleCount > 0;
        SaveRuntimeState();
    }

    private static string GetModeLabel(TimerMode mode, bool paused)
    {
        if (paused)
        {
            return "PAUSED";
        }

        return mode switch
        {
            TimerMode.Work => "WORK",
            TimerMode.ShortBreak => "SHORT BREAK",
            TimerMode.LongBreak => "LONG BREAK",
            _ => "IDLE"
        };
    }

    private void UpdateMeter(TimerSnapshot snapshot)
    {
        var durationSec = Math.Max(1.0, snapshot.Duration.TotalSeconds);
        var remainingSec = Math.Clamp(snapshot.Remaining.TotalSeconds, 0.0, durationSec);
        var ratio = snapshot.Duration.TotalSeconds <= 0.0 ? 0.0 : remainingSec / durationSec;

        if (snapshot.Mode == TimerMode.Idle && !snapshot.IsRunning)
        {
            ratio = 0.0;
        }

        MeterProgressPath.Stroke = new SolidColorBrush(GetConfiguredMeterColor());
        MeterProgressPath.Data = BuildMeterGeometry(ratio);
        MeterProgressPath.Visibility = ratio <= 0.001 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Geometry BuildMeterGeometry(double ratio)
    {
        if (ratio <= 0.001)
        {
            return Geometry.Empty;
        }

        ratio = Math.Clamp(ratio, 0.0, 1.0);
        var start = PointAtAngle(-90);

        if (ratio >= 0.999)
        {
            var firstHalf = PointAtAngle(90);
            var figure = new PathFigure
            {
                StartPoint = start,
                IsClosed = false,
                IsFilled = false
            };
            figure.Segments.Add(new ArcSegment(firstHalf, new System.Windows.Size(MeterRadius, MeterRadius), 0, false, SweepDirection.Clockwise, true));
            figure.Segments.Add(new ArcSegment(start, new System.Windows.Size(MeterRadius, MeterRadius), 0, false, SweepDirection.Clockwise, true));
            return new PathGeometry(new[] { figure });
        }

        var sweep = 360.0 * ratio;
        var end = PointAtAngle(-90 + sweep);
        var largeArc = sweep > 180.0;

        var path = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false
        };
        path.Segments.Add(new ArcSegment(end, new System.Windows.Size(MeterRadius, MeterRadius), 0, largeArc, SweepDirection.Clockwise, true));
        return new PathGeometry(new[] { path });
    }

    private static System.Windows.Point PointAtAngle(double degree)
    {
        var rad = degree * Math.PI / 180.0;
        return new System.Windows.Point(
            MeterCenter + (MeterRadius * Math.Cos(rad)),
            MeterCenter + (MeterRadius * Math.Sin(rad)));
    }

    private System.Windows.Media.Color GetConfiguredMeterColor()
    {
        return TryParseColor(_settings.MeterColorHex, out var parsed)
            ? parsed
            : System.Windows.Media.Color.FromRgb(0xF2, 0x8A, 0x4B);
    }

    private System.Windows.Media.Color GetConfiguredClockTextColor()
    {
        return TryParseColor(_settings.ClockTextColorHex, out var parsed)
            ? parsed
            : System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF);
    }

    private System.Windows.Media.Color GetConfiguredModeTextColor()
    {
        return TryParseColor(_settings.ModeTextColorHex, out var parsed)
            ? parsed
            : System.Windows.Media.Color.FromRgb(0xD8, 0xE9, 0xFF);
    }

    private void UpdatePrimaryActionButton(TimerSnapshot snapshot)
    {
        if (_pendingWorkMemoEntry is not null)
        {
            PrimaryActionButton.Content = "休憩開始";
            return;
        }

        if (!snapshot.IsRunning)
        {
            PrimaryActionButton.Content = "開始";
            return;
        }

        if (snapshot.IsPaused)
        {
            PrimaryActionButton.Content = "再開";
            return;
        }

        PrimaryActionButton.Content = "一時停止";
    }

    private void ApplyRuntimeSettings(PomodoroSettings settings)
    {
        Topmost = settings.AlwaysOnTop;
        ApplyClockFont(settings.ClockFontFamily);
        ApplyVisualColors();
        UpdateGhostMode();
    }

    private void ApplyVisualColors()
    {
        if (_isGhostMode)
        {
            return;
        }

        var clockBase = TryParseColor(_settings.ShellBackgroundColorHex, out var parsed)
            ? parsed
            : System.Windows.Media.Color.FromRgb(0x1D, 0x36, 0x5F);
        var alpha = (byte)Math.Round(Math.Clamp(_settings.ShellBackgroundOpacity, 0.1, 1.0) * 255.0);
        clockBase.A = alpha;
        ClockCard.Background = new SolidColorBrush(clockBase);

        var meter = GetConfiguredMeterColor();
        MeterTrack.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(110, meter.R, meter.G, meter.B));
        RemainingText.Foreground = new SolidColorBrush(GetConfiguredClockTextColor());
        ModeText.Foreground = new SolidColorBrush(GetConfiguredModeTextColor());
    }

    private static bool TryParseColor(string? hex, out System.Windows.Media.Color color)
    {
        color = System.Windows.Media.Color.FromRgb(0x1D, 0x36, 0x5F);
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var text = hex.Trim();
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        try
        {
            var obj = System.Windows.Media.ColorConverter.ConvertFromString(text);
            if (obj is System.Windows.Media.Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private void ApplyClockFont(string? fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName))
        {
            fontName = "Consolas";
        }

        try
        {
            var font = new System.Windows.Media.FontFamily(fontName);
            ModeText.FontFamily = font;
            RemainingText.FontFamily = font;
        }
        catch
        {
            var fallback = new System.Windows.Media.FontFamily("Consolas");
            ModeText.FontFamily = fallback;
            RemainingText.FontFamily = fallback;
        }
    }

    private void OpenSettingsDialog()
    {
        SetGhostMode(false);
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultSettings is null)
        {
            return;
        }

        _settings = dialog.ResultSettings;
        _settingsService.Save(_settings);
        _timerService.UpdateSettings(_settings);
        _audioService.UpdateSettings(_settings);
        RebuildTagSelector();
        ApplyRuntimeSettings(_settings);
        RenderSnapshot(_timerService.Snapshot);
    }

    private void UpdateGhostMode()
    {
        if (!_settings.EnableMouseLeaveOpacity)
        {
            SetGhostMode(false);
            return;
        }

        var shouldGhost = !IsActive && !IsMouseOver;
        SetGhostMode(shouldGhost);
    }

    private void SetGhostMode(bool ghost)
    {
        if (_isGhostMode == ghost)
        {
            return;
        }

        var desiredClockCenter = GetClockCenterOnScreenDip();
        _isGhostMode = ghost;
        _suspendBoundsCapture = true;

        try
        {
            if (ghost)
            {
                CaptureNormalBounds();

                HeaderPanel.Visibility = Visibility.Collapsed;
                ControlsPanel.Visibility = Visibility.Collapsed;
                TagPanel.Visibility = Visibility.Collapsed;
                NotePanel.Visibility = Visibility.Collapsed;
                ProgressPanel.Visibility = Visibility.Collapsed;
                StateHintText.Visibility = Visibility.Collapsed;

                MainStack.VerticalAlignment = VerticalAlignment.Center;
                MainStack.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                ShellCard.Background = System.Windows.Media.Brushes.Transparent;
                ShellCard.BorderBrush = System.Windows.Media.Brushes.Transparent;
                ShellCard.Padding = new Thickness(0);
                ShellCard.Margin = new Thickness(0);
                ShellCard.CornerRadius = new CornerRadius(0);

                Width = GhostWidth;
                Height = GhostHeight;
                MoveWindowToClockCenter(desiredClockCenter);
                EnsureWindowVisible(allowPartial: true);
                Opacity = 1.0;
                return;
            }

            HeaderPanel.Visibility = Visibility.Visible;
            ControlsPanel.Visibility = Visibility.Visible;
            TagPanel.Visibility = Visibility.Visible;
            NotePanel.Visibility = Visibility.Visible;
            ProgressPanel.Visibility = Visibility.Visible;
            StateHintText.Visibility = Visibility.Visible;

            MainStack.VerticalAlignment = VerticalAlignment.Stretch;
            MainStack.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            ShellCard.Background = (System.Windows.Media.Brush)FindResource("ShellBrush");
            ShellCard.BorderBrush = (System.Windows.Media.Brush)FindResource("ShellBorderBrush");
            ShellCard.Padding = new Thickness(16);
            ShellCard.Margin = new Thickness(10);
            ShellCard.CornerRadius = new CornerRadius(24);
            ApplyVisualColors();

            Width = Math.Max(_normalWidth, 380);
            Height = Math.Max(_normalHeight, 620);
            MoveWindowToClockCenter(desiredClockCenter);
            EnsureWindowVisible(allowPartial: true);
            Opacity = 1.0;
        }
        finally
        {
            _suspendBoundsCapture = false;
        }
    }

    private System.Windows.Point GetClockCenterOnScreenDip()
    {
        UpdateLayout();
        if (ClockHost.ActualWidth <= 0 || ClockHost.ActualHeight <= 0)
        {
            return new System.Windows.Point(Left + (Width / 2.0), Top + (Height / 2.0));
        }

        var inWindow = ClockHost.TranslatePoint(
            new System.Windows.Point(ClockHost.ActualWidth / 2.0, ClockHost.ActualHeight / 2.0),
            this);
        return new System.Windows.Point(Left + inWindow.X, Top + inWindow.Y);
    }

    private void MoveWindowToClockCenter(System.Windows.Point desiredClockCenter)
    {
        UpdateLayout();
        var current = GetClockCenterOnScreenDip();
        Left += desiredClockCenter.X - current.X;
        Top += desiredClockCenter.Y - current.Y;
    }

    private void CaptureNormalBounds()
    {
        if (_isGhostMode || _suspendBoundsCapture)
        {
            return;
        }

        if (!double.IsNaN(Width) && Width > 0)
        {
            _normalWidth = Width;
        }

        if (!double.IsNaN(Height) && Height > 0)
        {
            _normalHeight = Height;
        }

        if (!double.IsNaN(Left))
        {
            _normalLeft = Left;
        }

        if (!double.IsNaN(Top))
        {
            _normalTop = Top;
        }
    }

    private void EnsureWindowVisible(bool allowPartial)
    {
        var area = SystemParameters.WorkArea;

        if (allowPartial)
        {
            var maxLeft = area.Right - MinVisiblePixels;
            var maxTop = area.Bottom - MinVisiblePixels;
            var minLeft = area.Left + MinVisiblePixels - Width;
            var minTop = area.Top + MinVisiblePixels - Height;

            Left = Math.Clamp(Left, minLeft, maxLeft);
            Top = Math.Clamp(Top, minTop, maxTop);
            return;
        }

        Left = Math.Clamp(Left, area.Left, area.Right - Width);
        Top = Math.Clamp(Top, area.Top, area.Bottom - Height);
    }

    private void CenterWindowOnScreen()
    {
        SetGhostMode(false);
        var area = SystemParameters.WorkArea;
        var desired = new System.Windows.Point(
            area.Left + (area.Width / 2.0),
            area.Top + (area.Height / 2.0));
        MoveWindowToClockCenter(desired);
        EnsureWindowVisible(allowPartial: true);
    }

    private void RestoreRuntimeStateIfAvailable()
    {
        var state = _appStateService.Load();
        if (state is null)
        {
            return;
        }

        _timerService.Restore(state);
    }

    private void SaveRuntimeState()
    {
        _appStateService.Save(_timerService.ToRuntimeState());
    }

    private void RefreshProgress()
    {
        var sessions = _sessionLogService.ReadAll();
        var todayTotal = _statsService.GetDailyWorkTotal(sessions, DateOnly.FromDateTime(DateTime.Now));
        var monthTotal = _statsService.GetMonthlyWorkTotal(sessions, DateTime.Now.Year, DateTime.Now.Month);
        ProgressText.Text = $"今日: {todayTotal:hh\\:mm} | 今月: {monthTotal:hh\\:mm} | 完了サイクル: {_timerService.CycleCount}";
    }

    private void UpdateMemoInputUi()
    {
        var snapshot = _timerService.Snapshot;
        var waiting = _pendingWorkMemoEntry is not null;
        var allowInput = waiting || snapshot.Mode == TimerMode.Work;
        NoteTextBox.IsEnabled = allowInput;
        SetTagSelectionEnabled(allowInput);
        StateHintText.Text = waiting ? PendingMemoHint : DefaultHint;
    }

    private bool CommitPendingWorkMemo()
    {
        if (_pendingWorkMemoEntry is null)
        {
            return false;
        }

        _pendingWorkMemoEntry.Note = NoteTextBox.Text.Trim();
        _pendingWorkMemoEntry.TagIds = _selectedTagIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        _sessionLogService.Append(_pendingWorkMemoEntry);
        _pendingWorkMemoEntry = null;
        NoteTextBox.Text = string.Empty;
        RefreshProgress();
        UpdateMemoInputUi();
        return true;
    }

    private void PrimaryAction_Click(object sender, RoutedEventArgs e)
    {
        PrimaryAction();
    }

    private void PrimaryAction()
    {
        if (_pendingWorkMemoEntry is not null)
        {
            CommitPendingWorkMemo();
            var pendingSnapshot = _timerService.Snapshot;
            if (pendingSnapshot.IsRunning && pendingSnapshot.IsPaused)
            {
                _timerService.Resume();
            }
            else if (!pendingSnapshot.IsRunning)
            {
                _timerService.Start();
            }
            return;
        }

        var snapshot = _timerService.Snapshot;
        if (!snapshot.IsRunning)
        {
            _timerService.Start();
            return;
        }

        if (snapshot.IsPaused)
        {
            _timerService.Resume();
            return;
        }

        _timerService.Pause();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingWorkMemo();
        _timerService.Skip();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingWorkMemo();
        _timerService.Reset();
        RefreshProgress();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsDialog();
    }

    private void CloseToTray_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void OpenStats_Click(object sender, RoutedEventArgs e)
    {
        SetGhostMode(false);
        var window = new StatsWindow(_sessionLogService.ReadAll(), _settings.Tags, _exportService)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowExit)
        {
            _notifyIcon.Icon?.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _timerService.Dispose();
            _opacityTimer.Stop();
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        ShowBalloon("ポモドーロタイマー", "タスクトレイに格納しました。", 1200);
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        Activate();
        EnsureWindowVisible(allowPartial: true);
        SetGhostMode(false);
    }

    private void ExitApplication()
    {
        CommitPendingWorkMemo();
        SetGhostMode(false);
        var result = System.Windows.MessageBox.Show(
            this,
            "ポモドーロタイマーを終了しますか？",
            "ポモドーロタイマー",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _allowExit = true;
        SaveRuntimeState();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void RebuildTagSelector()
    {
        var activeTags = _settings.Tags
            .Where(x => !x.IsArchived && !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Name))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _selectedTagIds.IntersectWith(activeTags.Select(x => x.Id));
        _tagColorMap.Clear();
        TagWrapPanel.Children.Clear();
        NoTagText.Visibility = activeTags.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var tag in activeTags)
        {
            var tagColor = TryParseColor(tag.ColorHex, out var parsedTagColor)
                ? parsedTagColor
                : System.Windows.Media.Color.FromRgb(0x4B, 0x8B, 0xF4);
            _tagColorMap[tag.Id] = tagColor;

            var toggle = new ToggleButton
            {
                Style = (Style)FindResource("TagPillButton"),
                Tag = tag.Id,
                Content = tag.Name,
                IsChecked = _selectedTagIds.Contains(tag.Id),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, tagColor.R, tagColor.G, tagColor.B)),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, tagColor.R, tagColor.G, tagColor.B))
            };
            ApplyTagPillForeground(toggle, tagColor);

            toggle.Checked += TagToggleChanged;
            toggle.Unchecked += TagToggleChanged;
            TagWrapPanel.Children.Add(toggle);
        }

        UpdateMemoInputUi();
    }

    private void TagToggleChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle || toggle.Tag is not string tagId)
        {
            return;
        }

        if (toggle.IsChecked == true)
        {
            _selectedTagIds.Add(tagId);
        }
        else
        {
            _selectedTagIds.Remove(tagId);
        }

        if (_tagColorMap.TryGetValue(tagId, out var tagColor))
        {
            ApplyTagPillForeground(toggle, tagColor);
        }
    }

    private void SetTagSelectionEnabled(bool enabled)
    {
        foreach (var child in TagWrapPanel.Children.OfType<ToggleButton>())
        {
            child.IsEnabled = enabled;
        }
    }

    private void ClearSelectedTags()
    {
        _selectedTagIds.Clear();
        foreach (var child in TagWrapPanel.Children.OfType<ToggleButton>())
        {
            child.IsChecked = false;
        }
    }

    private void ShowBalloon(string title, string text, int timeoutMs)
    {
        if (!_settings.EnableSystemNotifications)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.ShowBalloonTip(timeoutMs);
    }

    private void ApplyTagPillForeground(ToggleButton toggle, System.Windows.Media.Color tagColor)
    {
        var isOn = toggle.IsChecked == true;
        var background = isOn ? tagColor : System.Windows.Media.Color.FromRgb(0x14, 0x1E, 0x31);
        var preferred = isOn
            ? GetReadableTextColor(background)
            : tagColor;

        // For OFF state, if tag color is hard to read on dark panel, fallback to high contrast.
        if (!isOn && GetContrastRatio(preferred, background) < 3.3)
        {
            preferred = GetReadableTextColor(background);
        }

        toggle.Foreground = new SolidColorBrush(preferred);
    }

    private static System.Windows.Media.Color GetReadableTextColor(System.Windows.Media.Color background)
    {
        var contrastWhite = GetContrastRatio(System.Windows.Media.Colors.White, background);
        var contrastDark = GetContrastRatio(System.Windows.Media.Color.FromRgb(0x0E, 0x16, 0x25), background);
        return contrastWhite >= contrastDark
            ? System.Windows.Media.Colors.White
            : System.Windows.Media.Color.FromRgb(0x0E, 0x16, 0x25);
    }

    private static double GetContrastRatio(System.Windows.Media.Color foreground, System.Windows.Media.Color background)
    {
        var l1 = RelativeLuminance(foreground);
        var l2 = RelativeLuminance(background);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(System.Windows.Media.Color color)
    {
        static double Lin(double channel)
        {
            var srgb = channel / 255.0;
            return srgb <= 0.03928
                ? srgb / 12.92
                : Math.Pow((srgb + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Lin(color.R)) + (0.7152 * Lin(color.G)) + (0.0722 * Lin(color.B));
    }
}
