using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WinPomTimer.Domain;
using WinPomTimer.Services;
using Forms = System.Windows.Forms;

namespace WinPomTimer;

public partial class SettingsWindow : Window
{
    private readonly PomodoroSettings _working;
    private readonly List<TaskTag> _tagDraft = new();
    public PomodoroSettings? ResultSettings { get; private set; }

    public SettingsWindow(PomodoroSettings currentSettings)
    {
        InitializeComponent();
        _working = currentSettings.Clone();
        _tagDraft.AddRange(_working.Tags.Select(x => x.Clone()));
        LoadFontOptions();
        LoadTagAxisOptions();
        ApplySettingsToUi(_working);
    }

    private void LoadFontOptions()
    {
        var names = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ClockFontCombo.ItemsSource = names;
    }

    private void LoadTagAxisOptions()
    {
        TagAxisCombo.ItemsSource = new[]
        {
            ToAxisLabel(TagAxis.WorkType),
            ToAxisLabel(TagAxis.Client),
            ToAxisLabel(TagAxis.Other)
        };
        TagAxisCombo.SelectedIndex = 0;
    }

    private void ApplySettingsToUi(PomodoroSettings settings)
    {
        WorkMinutesBox.Text = settings.WorkMinutes.ToString(CultureInfo.InvariantCulture);
        ShortBreakMinutesBox.Text = settings.ShortBreakMinutes.ToString(CultureInfo.InvariantCulture);
        LongBreakMinutesBox.Text = settings.LongBreakMinutes.ToString(CultureInfo.InvariantCulture);
        LongBreakIntervalBox.Text = settings.LongBreakInterval.ToString(CultureInfo.InvariantCulture);
        AutoStartBreakCheck.IsChecked = settings.AutoStartBreak;
        AutoStartWorkCheck.IsChecked = settings.AutoStartWork;

        MuteAllCheck.IsChecked = settings.MuteAll;
        SystemNotificationsCheck.IsChecked = settings.EnableSystemNotifications;
        MasterVolumeBox.Text = settings.MasterVolumePercent.ToString(CultureInfo.InvariantCulture);
        PreAlertSecondsBox.Text = settings.PreBreakAlertSeconds.ToString(CultureInfo.InvariantCulture);
        TickEnabledCheck.IsChecked = settings.TickSoundEnabled;
        TickVolumeBox.Text = settings.TickVolumePercent.ToString(CultureInfo.InvariantCulture);
        TickModeCombo.SelectedIndex = settings.TickSoundMode == TickSoundMode.WorkOnly ? 0 : 1;
        TickSoundPathBox.Text = settings.TickSoundPath ?? string.Empty;
        WorkEndSoundPathBox.Text = settings.WorkEndSoundPath ?? string.Empty;

        AlwaysOnTopCheck.IsChecked = settings.AlwaysOnTop;
        MouseLeaveOpacityCheck.IsChecked = settings.EnableMouseLeaveOpacity;
        MouseLeaveOpacityBox.Text = settings.MouseLeaveOpacity.ToString(CultureInfo.InvariantCulture);
        ShellColorBox.Text = settings.ShellBackgroundColorHex;
        ShellOpacityBox.Text = settings.ShellBackgroundOpacity.ToString(CultureInfo.InvariantCulture);
        MeterColorBox.Text = settings.MeterColorHex;
        ClockTextColorBox.Text = settings.ClockTextColorHex;
        ModeTextColorBox.Text = settings.ModeTextColorHex;
        ClockFontCombo.Text = settings.ClockFontFamily;
        RefreshTagList();
        ResetTagEditor();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _working.WorkMinutes = ParseInt(WorkMinutesBox.Text, 25, 1, 240);
        _working.ShortBreakMinutes = ParseInt(ShortBreakMinutesBox.Text, 5, 1, 120);
        _working.LongBreakMinutes = ParseInt(LongBreakMinutesBox.Text, 15, 1, 120);
        _working.LongBreakInterval = ParseInt(LongBreakIntervalBox.Text, 4, 1, 20);
        _working.AutoStartBreak = AutoStartBreakCheck.IsChecked == true;
        _working.AutoStartWork = AutoStartWorkCheck.IsChecked == true;

        _working.MuteAll = MuteAllCheck.IsChecked == true;
        _working.EnableSystemNotifications = SystemNotificationsCheck.IsChecked == true;
        _working.MasterVolumePercent = ParseInt(MasterVolumeBox.Text, 80, 0, 100);
        _working.PreBreakAlertSeconds = ParseInt(PreAlertSecondsBox.Text, 10, 0, 300);
        _working.TickSoundEnabled = TickEnabledCheck.IsChecked == true;
        _working.TickVolumePercent = ParseInt(TickVolumeBox.Text, 30, 0, 100);
        _working.TickSoundMode = TickModeCombo.SelectedIndex == 0 ? TickSoundMode.WorkOnly : TickSoundMode.AllSessions;
        _working.TickSoundPath = NormalizePath(TickSoundPathBox.Text);
        _working.WorkEndSoundPath = NormalizePath(WorkEndSoundPathBox.Text);

        _working.AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
        _working.EnableMouseLeaveOpacity = MouseLeaveOpacityCheck.IsChecked == true;
        _working.MouseLeaveOpacity = ParseDouble(MouseLeaveOpacityBox.Text, 1.0, 0.1, 1.0);
        _working.ShellBackgroundColorHex = NormalizeHex(ShellColorBox.Text, "#121826");
        _working.ShellBackgroundOpacity = ParseDouble(ShellOpacityBox.Text, 0.87, 0.1, 1.0);
        _working.MeterColorHex = NormalizeHex(MeterColorBox.Text, "#F28A4B");
        _working.ClockTextColorHex = NormalizeHex(ClockTextColorBox.Text, "#FFFFFF");
        _working.ModeTextColorHex = NormalizeHex(ModeTextColorBox.Text, "#D8E9FF");
        _working.ClockFontFamily = string.IsNullOrWhiteSpace(ClockFontCombo.Text) ? "Consolas" : ClockFontCombo.Text.Trim();
        _working.Tags = BuildTagsForSave();

        ResultSettings = _working;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static int ParseInt(string? value, int fallback, int min, int max)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return fallback;
        }

        return Math.Clamp(parsed, min, max);
    }

    private static double ParseDouble(string? value, double fallback, double min, double max)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return fallback;
        }

        return Math.Clamp(parsed, min, max);
    }

    private void BrowseTickSound_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "WAV files (*.wav)|*.wav|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true)
        {
            TickSoundPathBox.Text = dialog.FileName;
        }
    }

    private void BrowseWorkEndSound_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "WAV files (*.wav)|*.wav|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true)
        {
            WorkEndSoundPathBox.Text = dialog.FileName;
        }
    }

    private void PickShellColor_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectColor(ShellColorBox.Text);
        if (selected is not null)
        {
            ShellColorBox.Text = selected;
        }
    }

    private void PickMeterColor_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectColor(MeterColorBox.Text);
        if (selected is not null)
        {
            MeterColorBox.Text = selected;
        }
    }

    private void PickClockTextColor_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectColor(ClockTextColorBox.Text);
        if (selected is not null)
        {
            ClockTextColorBox.Text = selected;
        }
    }

    private void PickModeTextColor_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectColor(ModeTextColorBox.Text);
        if (selected is not null)
        {
            ModeTextColorBox.Text = selected;
        }
    }

    private void ResetDisplayDefaults_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new PomodoroSettings();
        ShellColorBox.Text = defaults.ShellBackgroundColorHex;
        ShellOpacityBox.Text = defaults.ShellBackgroundOpacity.ToString(CultureInfo.InvariantCulture);
        MeterColorBox.Text = defaults.MeterColorHex;
        ClockTextColorBox.Text = defaults.ClockTextColorHex;
        ModeTextColorBox.Text = defaults.ModeTextColorHex;
        ClockFontCombo.Text = defaults.ClockFontFamily;
    }

    private void TagListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var idx = TagListBox.SelectedIndex;
        if (idx < 0 || idx >= _tagDraft.Count)
        {
            return;
        }

        var tag = _tagDraft[idx];
        TagNameBox.Text = tag.Name;
        TagColorBox.Text = NormalizeHex(tag.ColorHex, "#4B8BF4");
        TagAxisCombo.SelectedIndex = (int)tag.Axis;
    }

    private void NewTag_Click(object sender, RoutedEventArgs e)
    {
        TagListBox.SelectedIndex = -1;
        ResetTagEditor();
    }

    private void AddOrUpdateTag_Click(object sender, RoutedEventArgs e)
    {
        var name = (TagNameBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show(this, "タグ名を入力してください。", "タグ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (name.Length > 24)
        {
            System.Windows.MessageBox.Show(this, "タグ名は24文字以内で入力してください。", "タグ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var colorHex = NormalizeHex(TagColorBox.Text, "#4B8BF4");
        var axis = GetSelectedAxis();
        var selectedIdx = TagListBox.SelectedIndex;
        var duplicate = _tagDraft
            .Where((_, idx) => idx != selectedIdx)
            .Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (duplicate)
        {
            System.Windows.MessageBox.Show(this, "同じ名前のタグが既に存在します。", "タグ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (selectedIdx >= 0 && selectedIdx < _tagDraft.Count)
        {
            _tagDraft[selectedIdx].Name = name;
            _tagDraft[selectedIdx].ColorHex = colorHex;
            _tagDraft[selectedIdx].Axis = axis;
            RefreshTagList(selectedIdx);
            return;
        }

        _tagDraft.Add(new TaskTag
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            ColorHex = colorHex,
            Axis = axis,
            IsArchived = false
        });
        RefreshTagList(_tagDraft.Count - 1);
    }

    private void DeleteTag_Click(object sender, RoutedEventArgs e)
    {
        var selectedIdx = TagListBox.SelectedIndex;
        if (selectedIdx < 0 || selectedIdx >= _tagDraft.Count)
        {
            return;
        }

        _tagDraft.RemoveAt(selectedIdx);
        RefreshTagList();
        ResetTagEditor();
    }

    private void ToggleArchiveTag_Click(object sender, RoutedEventArgs e)
    {
        var selectedIdx = TagListBox.SelectedIndex;
        if (selectedIdx < 0 || selectedIdx >= _tagDraft.Count)
        {
            return;
        }

        _tagDraft[selectedIdx].IsArchived = !_tagDraft[selectedIdx].IsArchived;
        RefreshTagList(selectedIdx);
    }

    private void PickTagColor_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectColor(TagColorBox.Text);
        if (selected is not null)
        {
            TagColorBox.Text = selected;
        }
    }

    private void RefreshTagList(int selectedIndex = -1)
    {
        var items = _tagDraft
            .Select(x => $"{x.Name} [{ToAxisLabel(x.Axis)}] ({NormalizeHex(x.ColorHex, "#4B8BF4")}){(x.IsArchived ? " [アーカイブ]" : string.Empty)}")
            .ToList();
        TagListBox.ItemsSource = items;

        if (items.Count == 0)
        {
            TagListBox.SelectedIndex = -1;
            return;
        }

        if (selectedIndex >= 0 && selectedIndex < items.Count)
        {
            TagListBox.SelectedIndex = selectedIndex;
            return;
        }

        TagListBox.SelectedIndex = 0;
    }

    private void ResetTagEditor()
    {
        TagNameBox.Text = string.Empty;
        TagAxisCombo.SelectedIndex = 0;
        TagColorBox.Text = "#4B8BF4";
    }

    private List<TaskTag> BuildTagsForSave()
    {
        var result = new List<TaskTag>();
        foreach (var tag in _tagDraft)
        {
            var name = (tag.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (result.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(new TaskTag
            {
                Id = string.IsNullOrWhiteSpace(tag.Id) ? Guid.NewGuid().ToString("N") : tag.Id,
                Name = name.Length > 24 ? name[..24] : name,
                ColorHex = NormalizeHex(tag.ColorHex, "#4B8BF4"),
                Axis = Enum.IsDefined(tag.Axis) ? tag.Axis : TagAxis.WorkType,
                IsArchived = tag.IsArchived
            });
        }

        return result
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private TagAxis GetSelectedAxis()
    {
        return TagAxisCombo.SelectedIndex switch
        {
            1 => TagAxis.Client,
            2 => TagAxis.Other,
            _ => TagAxis.WorkType
        };
    }

    private static string ToAxisLabel(TagAxis axis)
    {
        return axis switch
        {
            TagAxis.Client => "クライアント軸",
            TagAxis.Other => "その他",
            _ => "作業内容軸"
        };
    }

    private static string? SelectColor(string currentHex)
    {
        using var dialog = new Forms.ColorDialog();
        if (TryParseHex(currentHex, out var color))
        {
            dialog.Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
        }

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return null;
        }

        return $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    }

    private static string NormalizeHex(string? input, string fallback)
    {
        if (TryParseHex(input, out var color))
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        return fallback;
    }

    private static bool TryParseHex(string? input, out System.Windows.Media.Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        try
        {
            var converted = System.Windows.Media.ColorConverter.ConvertFromString(text);
            if (converted is System.Windows.Media.Color parsed)
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

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return path.Trim();
    }
}
