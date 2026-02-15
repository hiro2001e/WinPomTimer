using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinPomTimer.Domain;
using WinPomTimer.Services;

namespace WinPomTimer;

public partial class StatsWindow : Window
{
    private readonly IReadOnlyList<SessionLogEntry> _sessions;
    private readonly Dictionary<string, TaskTag> _tagsById;
    private readonly ExportService _exportService;
    private readonly StatsService _statsService = new();

    private IReadOnlyList<StackedSeriesBucket> _currentBuckets = Array.Empty<StackedSeriesBucket>();
    private readonly Dictionary<string, SolidColorBrush> _categoryBrushes = new(StringComparer.OrdinalIgnoreCase);
    private string _chartTitle = string.Empty;
    private DateOnly _anchorDate = DateOnly.FromDateTime(DateTime.Now);

    public StatsWindow(IReadOnlyList<SessionLogEntry> sessions, IEnumerable<TaskTag> tags, ExportService exportService)
    {
        InitializeComponent();
        _sessions = sessions;
        _tagsById = tags
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);
        _exportService = exportService;

        Loaded += (_, _) =>
        {
            GraphTypeCombo.SelectedIndex = 0;
            AxisModeCombo.SelectedIndex = 0;
            _anchorDate = DateOnly.FromDateTime(DateTime.Now);
            RefreshView();
        };
        ChartCanvas.SizeChanged += (_, _) => RenderChart(_currentBuckets, _chartTitle);
    }

    private void GraphTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshView();
    }

    private void AxisModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshView();
    }

    private void PrevRange_Click(object sender, RoutedEventArgs e)
    {
        switch (GetGraphMode())
        {
            case GraphMode.Day:
                _anchorDate = _anchorDate.AddDays(-1);
                break;
            case GraphMode.Week:
                _anchorDate = _anchorDate.AddDays(-7);
                break;
            case GraphMode.Month:
                _anchorDate = _anchorDate.AddMonths(-1);
                break;
            case GraphMode.Year:
                _anchorDate = _anchorDate.AddYears(-1);
                break;
            default:
                return;
        }

        RefreshView();
    }

    private void NextRange_Click(object sender, RoutedEventArgs e)
    {
        switch (GetGraphMode())
        {
            case GraphMode.Day:
                _anchorDate = _anchorDate.AddDays(1);
                break;
            case GraphMode.Week:
                _anchorDate = _anchorDate.AddDays(7);
                break;
            case GraphMode.Month:
                _anchorDate = _anchorDate.AddMonths(1);
                break;
            case GraphMode.Year:
                _anchorDate = _anchorDate.AddYears(1);
                break;
            default:
                return;
        }

        RefreshView();
    }

    private void TodayRange_Click(object sender, RoutedEventArgs e)
    {
        _anchorDate = DateOnly.FromDateTime(DateTime.Now);
        RefreshView();
    }

    private void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        ExportWithDialog("JSON ファイル (*.json)|*.json", _exportService.ExportJson);
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        ExportWithDialog("CSV ファイル (*.csv)|*.csv", _exportService.ExportCsv);
    }

    private void ExportIcs_Click(object sender, RoutedEventArgs e)
    {
        ExportWithDialog("ICS ファイル (*.ics)|*.ics", _exportService.ExportIcs);
    }

    private void ExportWithDialog(string filter, Action<IEnumerable<SessionLogEntry>, string> exportAction)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = filter,
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        exportAction(_sessions, dialog.FileName);
        System.Windows.MessageBox.Show(this, "エクスポートが完了しました。", "統計ダッシュボード", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RefreshView()
    {
        var anchorDateTime = _anchorDate.ToDateTime(TimeOnly.MinValue);
        var mode = GetGraphMode();
        var axisMode = GetAxisMode();
        var navEnabled = mode != GraphMode.None;
        PrevRangeButton.IsEnabled = navEnabled;
        NextRangeButton.IsEnabled = navEnabled;
        TodayRangeButton.IsEnabled = navEnabled;

        if (mode == GraphMode.None)
        {
            _currentBuckets = Array.Empty<StackedSeriesBucket>();
            _chartTitle = string.Empty;
            RangeLabelText.Text = "全期間";
            BuildCategoryBrushes(Array.Empty<StackedSeriesBucket>(), axisMode);
            RenderChart(_currentBuckets, _chartTitle);
            var allWork = GetWorkSessions(null, null);
            BindCategorySummary(_statsService.BuildAxisSummary(_sessions, _tagsById, axisMode), allWork, axisMode);
            return;
        }

        DateOnly rangeStart;
        DateOnly rangeEnd;
        switch (mode)
        {
            case GraphMode.Day:
                _chartTitle = $"{_anchorDate:yyyy-MM-dd} 時間帯別";
                _currentBuckets = _statsService.BuildStackedDailySeries(_sessions, _tagsById, axisMode, _anchorDate);
                rangeStart = _anchorDate;
                rangeEnd = _anchorDate;
                break;
            case GraphMode.Week:
                rangeStart = StatsService.GetStartOfWeek(_anchorDate);
                rangeEnd = rangeStart.AddDays(6);
                _chartTitle = $"{rangeStart:yyyy-MM-dd} - {rangeEnd:yyyy-MM-dd} 週次";
                _currentBuckets = _statsService.BuildStackedWeeklySeries(_sessions, _tagsById, axisMode, _anchorDate);
                break;
            case GraphMode.Month:
                rangeStart = new DateOnly(anchorDateTime.Year, anchorDateTime.Month, 1);
                rangeEnd = rangeStart.AddMonths(1).AddDays(-1);
                _chartTitle = $"{anchorDateTime:yyyy-MM} 月次";
                _currentBuckets = _statsService.BuildStackedMonthlySeries(_sessions, _tagsById, axisMode, anchorDateTime.Year, anchorDateTime.Month);
                break;
            case GraphMode.Year:
                rangeStart = new DateOnly(anchorDateTime.Year, 1, 1);
                rangeEnd = new DateOnly(anchorDateTime.Year, 12, 31);
                _chartTitle = $"{anchorDateTime:yyyy} 年次";
                _currentBuckets = _statsService.BuildStackedYearlySeries(_sessions, _tagsById, axisMode, anchorDateTime.Year);
                break;
            default:
                _currentBuckets = Array.Empty<StackedSeriesBucket>();
                _chartTitle = string.Empty;
                rangeStart = _anchorDate;
                rangeEnd = _anchorDate;
                break;
        }

        RangeLabelText.Text = $"{rangeStart:yyyy-MM-dd} - {rangeEnd:yyyy-MM-dd}";
        BuildCategoryBrushes(_currentBuckets, axisMode);
        RenderChart(_currentBuckets, _chartTitle);
        var inRangeWork = GetWorkSessions(rangeStart, rangeEnd);
        BindCategorySummary(_statsService.BuildAxisSummary(_sessions, _tagsById, axisMode, rangeStart, rangeEnd), inRangeWork, axisMode);
    }

    private void RenderChart(IReadOnlyList<StackedSeriesBucket> series, string title)
    {
        ChartCanvas.Children.Clear();
        if (series.Count == 0)
        {
            ChartPlaceholderText.Visibility = Visibility.Visible;
            return;
        }

        ChartPlaceholderText.Visibility = Visibility.Collapsed;
        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;
        if (width < 120 || height < 120)
        {
            return;
        }

        const double left = 42;
        const double right = 10;
        const double top = 24;
        const double bottom = 30;

        var plotWidth = width - left - right;
        var plotHeight = height - top - bottom;
        if (plotWidth < 20 || plotHeight < 20)
        {
            return;
        }

        var maxValue = Math.Max(1.0, series.Max(x => x.TotalMinutes));
        var xAxis = new System.Windows.Shapes.Line
        {
            X1 = left,
            Y1 = top + plotHeight,
            X2 = left + plotWidth,
            Y2 = top + plotHeight,
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x86, 0x98, 0xBB)),
            StrokeThickness = 1
        };
        ChartCanvas.Children.Add(xAxis);

        var yAxis = new System.Windows.Shapes.Line
        {
            X1 = left,
            Y1 = top,
            X2 = left,
            Y2 = top + plotHeight,
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x86, 0x98, 0xBB)),
            StrokeThickness = 1
        };
        ChartCanvas.Children.Add(yAxis);

        AddCanvasText(left + 4, 2, title, 13, FontWeights.SemiBold, System.Windows.Media.Color.FromRgb(0x36, 0x4A, 0x6D));
        AddCanvasText(2, top - 6, $"{maxValue:0}m", 10, FontWeights.Normal, System.Windows.Media.Color.FromRgb(0x86, 0x98, 0xBB));
        AddCanvasText(10, top + plotHeight - 10, "0m", 10, FontWeights.Normal, System.Windows.Media.Color.FromRgb(0x86, 0x98, 0xBB));

        var count = series.Count;
        var stepX = plotWidth / count;
        var barWidth = Math.Max(4.0, stepX * 0.62);
        var labelSkip = count <= 12 ? 1 : (count <= 24 ? 2 : 3);
        var categoryOrder = series
            .SelectMany(x => x.Values)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Category = g.Key, Minutes = g.Sum(v => v.Value) })
            .OrderByDescending(x => x.Minutes)
            .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Category)
            .ToList();

        for (var i = 0; i < count; i++)
        {
            var bucket = series[i];
            var x = left + (i * stepX) + ((stepX - barWidth) / 2.0);
            var cursorY = top + plotHeight;

            foreach (var category in categoryOrder)
            {
                if (!bucket.Values.TryGetValue(category, out var value) || value <= 0.0)
                {
                    continue;
                }

                var ratio = Math.Clamp(value / maxValue, 0.0, 1.0);
                var segmentHeight = ratio * plotHeight;
                if (segmentHeight <= 0.0)
                {
                    continue;
                }

                cursorY -= segmentHeight;
                var bar = new System.Windows.Shapes.Rectangle
                {
                    Width = barWidth,
                    Height = segmentHeight,
                    RadiusX = 1.5,
                    RadiusY = 1.5,
                    Fill = ResolveCategoryBrush(category)
                };
                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, cursorY);
                ChartCanvas.Children.Add(bar);
            }

            if (i % labelSkip == 0 || i == count - 1)
            {
                AddCanvasText(x - 2, top + plotHeight + 4, bucket.Label, 9, FontWeights.Normal, System.Windows.Media.Color.FromRgb(0x5E, 0x6F, 0x90));
            }
        }
    }

    private void AddCanvasText(double x, double y, string text, double fontSize, FontWeight fontWeight, System.Windows.Media.Color color)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = fontWeight,
            Foreground = new SolidColorBrush(color)
        };
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        ChartCanvas.Children.Add(tb);
    }

    private void BindCategorySummary(IReadOnlyList<AxisSummaryPoint> points, IReadOnlyList<SessionLogEntry> scopedWorkSessions, AxisAggregationMode axisMode)
    {
        EnsureCategoryBrushes(points.Select(p => p.Category), axisMode);
        var rows = points.Select(p => new TagSummaryRow
        {
            TagName = p.Category,
            TotalTimeText = FormatMinutes(p.TotalMinutes),
            Count = p.Count,
            CategoryBrush = ResolveCategoryBrush(p.Category)
        }).ToList();
        TagSummaryGrid.ItemsSource = rows;
        SummaryAxisText.Text = $"軸: {GetAxisLabel(axisMode)}";

        var totalMinutes = scopedWorkSessions.Sum(x => Math.Max(0.0, (x.EndAt - x.StartAt).TotalMinutes));
        TagSummaryTotalText.Text = $"総計: {FormatMinutes(totalMinutes)} ({Math.Round(totalMinutes):0}分)";
    }

    private void BuildCategoryBrushes(IReadOnlyList<StackedSeriesBucket> buckets, AxisAggregationMode axisMode)
    {
        var categories = buckets
            .SelectMany(x => x.Values.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        EnsureCategoryBrushes(categories, axisMode);
    }

    private void EnsureCategoryBrushes(IEnumerable<string> categories, AxisAggregationMode axisMode)
    {
        foreach (var category in categories.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (_categoryBrushes.ContainsKey(category))
            {
                continue;
            }

            var color = ResolveCategoryColor(category, axisMode);
            _categoryBrushes[category] = new SolidColorBrush(color);
        }
    }

    private SolidColorBrush ResolveCategoryBrush(string category)
    {
        if (_categoryBrushes.TryGetValue(category, out var brush))
        {
            return brush;
        }

        var fallback = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6D, 0x7F, 0xA8));
        _categoryBrushes[category] = fallback;
        return fallback;
    }

    private System.Windows.Media.Color ResolveCategoryColor(string category, AxisAggregationMode axisMode)
    {
        if (axisMode == AxisAggregationMode.WorkType)
        {
            if (string.Equals(category, StatsService.UncategorizedWorkCategory, StringComparison.OrdinalIgnoreCase))
            {
                return System.Windows.Media.Color.FromRgb(0x79, 0x86, 0x9A);
            }

            if (TryGetTagColorByName(TagAxis.WorkType, category, out var workColor))
            {
                return workColor;
            }
        }
        else if (axisMode == AxisAggregationMode.Client)
        {
            if (string.Equals(category, StatsService.SelfClientCategory, StringComparison.OrdinalIgnoreCase))
            {
                return System.Windows.Media.Color.FromRgb(0x62, 0x81, 0xA8);
            }

            if (TryGetTagColorByName(TagAxis.Client, category, out var clientColor))
            {
                return clientColor;
            }
        }
        else
        {
            var split = category.Split(" x ", StringSplitOptions.TrimEntries);
            var clientName = split.Length > 0 ? split[0] : string.Empty;
            var workTypeName = split.Length > 1 ? split[1] : string.Empty;

            var baseColor = TryGetTagColorByName(TagAxis.WorkType, workTypeName, out var wtColor)
                ? wtColor
                : HashToColor(workTypeName);
            if (TryGetTagColorByName(TagAxis.Client, clientName, out var clColor))
            {
                return BlendColor(baseColor, clColor, 0.35);
            }

            return baseColor;
        }

        return HashToColor(category);
    }

    private bool TryGetTagColorByName(TagAxis axis, string name, out System.Windows.Media.Color color)
    {
        color = System.Windows.Media.Color.FromRgb(0x6D, 0x7F, 0xA8);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var tag = _tagsById.Values.FirstOrDefault(x =>
            !x.IsArchived &&
            x.Axis == axis &&
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (tag is null)
        {
            return false;
        }

        try
        {
            var parsed = System.Windows.Media.ColorConverter.ConvertFromString(tag.ColorHex);
            if (parsed is System.Windows.Media.Color c)
            {
                color = c;
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static System.Windows.Media.Color HashToColor(string text)
    {
        var value = 2166136261;
        foreach (var ch in text ?? string.Empty)
        {
            value ^= ch;
            value *= 16777619;
        }

        var hue = value % 360;
        return HsvToColor(hue, 0.58, 0.86);
    }

    private static System.Windows.Media.Color BlendColor(System.Windows.Media.Color a, System.Windows.Media.Color b, double weightB)
    {
        var wb = Math.Clamp(weightB, 0.0, 1.0);
        var wa = 1.0 - wb;
        byte Blend(byte x, byte y) => (byte)Math.Clamp(Math.Round((x * wa) + (y * wb)), 0, 255);
        return System.Windows.Media.Color.FromRgb(
            Blend(a.R, b.R),
            Blend(a.G, b.G),
            Blend(a.B, b.B));
    }

    private static System.Windows.Media.Color HsvToColor(double hue, double saturation, double value)
    {
        var h = (hue % 360 + 360) % 360;
        var s = Math.Clamp(saturation, 0.0, 1.0);
        var v = Math.Clamp(value, 0.0, 1.0);
        var c = v * s;
        var x = c * (1.0 - Math.Abs(((h / 60.0) % 2.0) - 1.0));
        var m = v - c;

        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        return System.Windows.Media.Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static string FormatMinutes(double minutes)
    {
        var span = TimeSpan.FromMinutes(Math.Max(0, minutes));
        return span.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
    }

    private IReadOnlyList<SessionLogEntry> GetWorkSessions(DateOnly? startInclusive, DateOnly? endInclusive)
    {
        var work = _sessions.Where(x => x.Mode == TimerMode.Work);
        if (startInclusive.HasValue && endInclusive.HasValue)
        {
            var start = startInclusive.Value;
            var end = endInclusive.Value;
            work = work.Where(x =>
            {
                var date = DateOnly.FromDateTime(x.StartAt.LocalDateTime);
                return date >= start && date <= end;
            });
        }

        return work.ToList();
    }

    private GraphMode GetGraphMode()
    {
        return GraphTypeCombo.SelectedIndex switch
        {
            1 => GraphMode.Day,
            2 => GraphMode.Week,
            3 => GraphMode.Month,
            4 => GraphMode.Year,
            _ => GraphMode.None
        };
    }

    private AxisAggregationMode GetAxisMode()
    {
        return AxisModeCombo.SelectedIndex switch
        {
            1 => AxisAggregationMode.Client,
            2 => AxisAggregationMode.ClientWorkType,
            _ => AxisAggregationMode.WorkType
        };
    }

    private static string GetAxisLabel(AxisAggregationMode mode)
    {
        return mode switch
        {
            AxisAggregationMode.Client => "クライアント",
            AxisAggregationMode.ClientWorkType => "クライアント×作業",
            _ => "作業内容"
        };
    }

    private enum GraphMode
    {
        None = 0,
        Day = 1,
        Week = 2,
        Month = 3,
        Year = 4
    }

    private sealed class TagSummaryRow
    {
        public string TagName { get; set; } = string.Empty;
        public string TotalTimeText { get; set; } = "00:00";
        public int Count { get; set; }
        public SolidColorBrush CategoryBrush { get; set; } = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6D, 0x7F, 0xA8));
    }
}
