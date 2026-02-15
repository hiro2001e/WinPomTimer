using WinPomTimer.Domain;

namespace WinPomTimer.Services;

public sealed class StatsService
{
    public const string SelfClientCategory = "Self";
    public const string UncategorizedWorkCategory = "Uncategorized";

    public TimeSpan GetDailyWorkTotal(IEnumerable<SessionLogEntry> sessions, DateOnly date)
    {
        var target = sessions.Where(s => s.Mode == TimerMode.Work && DateOnly.FromDateTime(s.StartAt.LocalDateTime) == date);
        return Sum(target);
    }

    public TimeSpan GetMonthlyWorkTotal(IEnumerable<SessionLogEntry> sessions, int year, int month)
    {
        var target = sessions.Where(s =>
            s.Mode == TimerMode.Work &&
            s.StartAt.LocalDateTime.Year == year &&
            s.StartAt.LocalDateTime.Month == month);
        return Sum(target);
    }

    public IReadOnlyList<TimeSeriesPoint> BuildDailySeries(IEnumerable<SessionLogEntry> sessions, DateOnly date)
    {
        var work = FilterWork(sessions);
        var values = new double[24];
        foreach (var entry in work.Where(x => DateOnly.FromDateTime(x.StartAt.LocalDateTime) == date))
        {
            var hour = Math.Clamp(entry.StartAt.LocalDateTime.Hour, 0, 23);
            values[hour] += GetDurationMinutes(entry);
        }

        var points = new List<TimeSeriesPoint>(24);
        for (var hour = 0; hour < 24; hour++)
        {
            points.Add(new TimeSeriesPoint($"{hour:00}", values[hour]));
        }

        return points;
    }

    public IReadOnlyList<TimeSeriesPoint> BuildWeeklySeries(IEnumerable<SessionLogEntry> sessions, DateOnly anyDayInWeek)
    {
        var start = GetStartOfWeek(anyDayInWeek);
        var values = new double[7];
        var work = FilterWork(sessions);
        foreach (var entry in work)
        {
            var day = DateOnly.FromDateTime(entry.StartAt.LocalDateTime);
            var diff = day.DayNumber - start.DayNumber;
            if (diff < 0 || diff > 6)
            {
                continue;
            }

            values[diff] += GetDurationMinutes(entry);
        }

        var labels = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        var points = new List<TimeSeriesPoint>(7);
        for (var i = 0; i < 7; i++)
        {
            points.Add(new TimeSeriesPoint(labels[i], values[i]));
        }

        return points;
    }

    public IReadOnlyList<TimeSeriesPoint> BuildMonthlySeries(IEnumerable<SessionLogEntry> sessions, int year, int month)
    {
        var days = DateTime.DaysInMonth(year, month);
        var values = new double[days];
        var work = FilterWork(sessions);
        foreach (var entry in work.Where(x => x.StartAt.LocalDateTime.Year == year && x.StartAt.LocalDateTime.Month == month))
        {
            var dayIndex = entry.StartAt.LocalDateTime.Day - 1;
            if (dayIndex < 0 || dayIndex >= days)
            {
                continue;
            }

            values[dayIndex] += GetDurationMinutes(entry);
        }

        var points = new List<TimeSeriesPoint>(days);
        for (var day = 1; day <= days; day++)
        {
            points.Add(new TimeSeriesPoint(day.ToString(), values[day - 1]));
        }

        return points;
    }

    public IReadOnlyList<TimeSeriesPoint> BuildYearlySeries(IEnumerable<SessionLogEntry> sessions, int year)
    {
        var values = new double[12];
        var work = FilterWork(sessions);
        foreach (var entry in work.Where(x => x.StartAt.LocalDateTime.Year == year))
        {
            var monthIndex = entry.StartAt.LocalDateTime.Month - 1;
            if (monthIndex < 0 || monthIndex >= 12)
            {
                continue;
            }

            values[monthIndex] += GetDurationMinutes(entry);
        }

        var labels = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        var points = new List<TimeSeriesPoint>(12);
        for (var i = 0; i < 12; i++)
        {
            points.Add(new TimeSeriesPoint(labels[i], values[i]));
        }

        return points;
    }

    public IReadOnlyList<TagSummaryPoint> BuildTagSummary(IEnumerable<SessionLogEntry> sessions)
    {
        return BuildTagSummaryInternal(FilterWork(sessions));
    }

    public IReadOnlyList<TagSummaryPoint> BuildTagSummary(IEnumerable<SessionLogEntry> sessions, DateOnly startInclusive, DateOnly endInclusive)
    {
        var target = FilterWork(sessions)
            .Where(x =>
            {
                var date = DateOnly.FromDateTime(x.StartAt.LocalDateTime);
                return date >= startInclusive && date <= endInclusive;
            });
        return BuildTagSummaryInternal(target);
    }

    public IReadOnlyList<AxisSummaryPoint> BuildAxisSummary(
        IEnumerable<SessionLogEntry> sessions,
        IReadOnlyDictionary<string, TaskTag> tagsById,
        AxisAggregationMode axisMode)
    {
        return BuildAxisSummaryInternal(FilterWork(sessions), tagsById, axisMode);
    }

    public IReadOnlyList<AxisSummaryPoint> BuildAxisSummary(
        IEnumerable<SessionLogEntry> sessions,
        IReadOnlyDictionary<string, TaskTag> tagsById,
        AxisAggregationMode axisMode,
        DateOnly startInclusive,
        DateOnly endInclusive)
    {
        var target = FilterWork(sessions)
            .Where(x =>
            {
                var date = DateOnly.FromDateTime(x.StartAt.LocalDateTime);
                return date >= startInclusive && date <= endInclusive;
            });
        return BuildAxisSummaryInternal(target, tagsById, axisMode);
    }

    public IReadOnlyList<StackedSeriesBucket> BuildStackedDailySeries(
        IEnumerable<SessionLogEntry> sessions,
        IReadOnlyDictionary<string, TaskTag> tagsById,
        AxisAggregationMode axisMode,
        DateOnly date)
    {
        var labels = Enumerable.Range(0, 24)
            .Select(h => h.ToString("00"))
            .ToArray();
        var values = CreateBucketMaps(labels.Length);
        foreach (var entry in FilterWork(sessions).Where(x => DateOnly.FromDateTime(x.StartAt.LocalDateTime) == date))
        {
            var idx = Math.Clamp(entry.StartAt.LocalDateTime.Hour, 0, 23);
            AddAllocatedToBucket(values[idx], entry, tagsById, axisMode);
        }

        return BuildStackedBuckets(labels, values);
    }

    public IReadOnlyList<StackedSeriesBucket> BuildStackedWeeklySeries(
        IEnumerable<SessionLogEntry> sessions,
        IReadOnlyDictionary<string, TaskTag> tagsById,
        AxisAggregationMode axisMode,
        DateOnly anyDayInWeek)
    {
        var start = GetStartOfWeek(anyDayInWeek);
        var labels = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        var values = CreateBucketMaps(labels.Length);
        foreach (var entry in FilterWork(sessions))
        {
            var day = DateOnly.FromDateTime(entry.StartAt.LocalDateTime);
            var diff = day.DayNumber - start.DayNumber;
            if (diff < 0 || diff > 6)
            {
                continue;
            }

            AddAllocatedToBucket(values[diff], entry, tagsById, axisMode);
        }

        return BuildStackedBuckets(labels, values);
    }

    public IReadOnlyList<StackedSeriesBucket> BuildStackedMonthlySeries(
        IEnumerable<SessionLogEntry> sessions,
        IReadOnlyDictionary<string, TaskTag> tagsById,
        AxisAggregationMode axisMode,
        int year,
        int month)
    {
        var days = DateTime.DaysInMonth(year, month);
        var labels = Enumerable.Range(1, days)
            .Select(d => d.ToString())
            .ToArray();
        var values = CreateBucketMaps(days);
        foreach (var entry in FilterWork(sessions).Where(x => x.StartAt.LocalDateTime.Year == year && x.StartAt.LocalDateTime.Month == month))
        {
            var idx = entry.StartAt.LocalDateTime.Day - 1;
            if (idx < 0 || idx >= days)
            {
                continue;
            }

            AddAllocatedToBucket(values[idx], entry, tagsById, axisMode);
        }

        return BuildStackedBuckets(labels, values);
    }

    public IReadOnlyList<StackedSeriesBucket> BuildStackedYearlySeries(
        IEnumerable<SessionLogEntry> sessions,
        IReadOnlyDictionary<string, TaskTag> tagsById,
        AxisAggregationMode axisMode,
        int year)
    {
        var labels = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        var values = CreateBucketMaps(12);
        foreach (var entry in FilterWork(sessions).Where(x => x.StartAt.LocalDateTime.Year == year))
        {
            var idx = entry.StartAt.LocalDateTime.Month - 1;
            if (idx < 0 || idx >= 12)
            {
                continue;
            }

            AddAllocatedToBucket(values[idx], entry, tagsById, axisMode);
        }

        return BuildStackedBuckets(labels, values);
    }

    private static IReadOnlyList<TagSummaryPoint> BuildTagSummaryInternal(IEnumerable<SessionLogEntry> sessions)
    {
        var map = new Dictionary<string, TagSummaryPoint>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in sessions)
        {
            var minutes = GetDurationMinutes(entry);
            var tags = entry.TagIds ?? new List<string>();
            if (tags.Count == 0)
            {
                const string noTagKey = "__untagged__";
                if (!map.TryGetValue(noTagKey, out var point))
                {
                    point = new TagSummaryPoint(noTagKey, minutes, 1);
                    map[noTagKey] = point;
                }
                else
                {
                    point.TotalMinutes += minutes;
                    point.Count += 1;
                }

                continue;
            }

            foreach (var tagId in tags.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!map.TryGetValue(tagId, out var point))
                {
                    point = new TagSummaryPoint(tagId, minutes, 1);
                    map[tagId] = point;
                }
                else
                {
                    point.TotalMinutes += minutes;
                    point.Count += 1;
                }
            }
        }

        return map.Values
            .OrderByDescending(x => x.TotalMinutes)
            .ThenBy(x => x.TagId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<AxisSummaryPoint> BuildAxisSummaryInternal(
        IEnumerable<SessionLogEntry> sessions,
        IReadOnlyDictionary<string, TaskTag> tagsById,
        AxisAggregationMode axisMode)
    {
        var map = new Dictionary<string, AxisSummaryPoint>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in sessions)
        {
            var categories = GetAllocatedCategories(entry, tagsById, axisMode);
            if (categories.Count == 0)
            {
                continue;
            }

            var total = GetDurationMinutes(entry);
            var perCategory = total / categories.Count;
            foreach (var category in categories)
            {
                if (!map.TryGetValue(category, out var point))
                {
                    point = new AxisSummaryPoint(category, 0.0, 0);
                    map[category] = point;
                }

                point.TotalMinutes += perCategory;
                point.Count += 1;
            }
        }

        return map.Values
            .OrderByDescending(x => x.TotalMinutes)
            .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> GetAllocatedCategories(
        SessionLogEntry entry,
        IReadOnlyDictionary<string, TaskTag> tagsById,
        AxisAggregationMode axisMode)
    {
        var selectedTags = (entry.TagIds ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => tagsById.TryGetValue(id, out var tag) ? tag : null)
            .Where(x => x is not null)
            .Cast<TaskTag>()
            .ToList();

        var clients = selectedTags
            .Where(x => x.Axis == TagAxis.Client)
            .Select(x => x.Name.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (clients.Count == 0)
        {
            clients.Add(SelfClientCategory);
        }

        var workTypes = selectedTags
            .Where(x => x.Axis == TagAxis.WorkType)
            .Select(x => x.Name.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (workTypes.Count == 0)
        {
            workTypes.Add(UncategorizedWorkCategory);
        }

        return axisMode switch
        {
            AxisAggregationMode.Client => clients,
            AxisAggregationMode.ClientWorkType => BuildCrossCategory(clients, workTypes),
            _ => workTypes
        };
    }

    private static IReadOnlyList<string> BuildCrossCategory(IReadOnlyList<string> clients, IReadOnlyList<string> workTypes)
    {
        var result = new List<string>(clients.Count * workTypes.Count);
        foreach (var client in clients)
        {
            foreach (var workType in workTypes)
            {
                result.Add($"{client} x {workType}");
            }
        }

        return result;
    }

    private static Dictionary<string, double>[] CreateBucketMaps(int count)
    {
        var values = new Dictionary<string, double>[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        return values;
    }

    private static IReadOnlyList<StackedSeriesBucket> BuildStackedBuckets(string[] labels, Dictionary<string, double>[] values)
    {
        var buckets = new List<StackedSeriesBucket>(labels.Length);
        for (var i = 0; i < labels.Length; i++)
        {
            buckets.Add(new StackedSeriesBucket(labels[i], values[i]));
        }

        return buckets;
    }

    private static void AddAllocatedToBucket(
        Dictionary<string, double> bucket,
        SessionLogEntry entry,
        IReadOnlyDictionary<string, TaskTag> tagsById,
        AxisAggregationMode axisMode)
    {
        var categories = GetAllocatedCategories(entry, tagsById, axisMode);
        if (categories.Count == 0)
        {
            return;
        }

        var total = GetDurationMinutes(entry);
        var perCategory = total / categories.Count;
        foreach (var category in categories)
        {
            if (bucket.TryGetValue(category, out var existing))
            {
                bucket[category] = existing + perCategory;
            }
            else
            {
                bucket[category] = perCategory;
            }
        }
    }

    public static DateOnly GetStartOfWeek(DateOnly date)
    {
        var dow = (int)date.DayOfWeek;
        var mondayBased = dow == 0 ? 6 : dow - 1;
        return date.AddDays(-mondayBased);
    }

    private static IEnumerable<SessionLogEntry> FilterWork(IEnumerable<SessionLogEntry> sessions)
    {
        return sessions.Where(s => s.Mode == TimerMode.Work);
    }

    private static TimeSpan Sum(IEnumerable<SessionLogEntry> entries)
    {
        var seconds = entries.Sum(x => Math.Max(0, (x.EndAt - x.StartAt).TotalSeconds));
        return TimeSpan.FromSeconds(seconds);
    }

    private static double GetDurationMinutes(SessionLogEntry entry)
    {
        return Math.Max(0.0, (entry.EndAt - entry.StartAt).TotalSeconds / 60.0);
    }
}

public sealed class TimeSeriesPoint
{
    public TimeSeriesPoint(string label, double minutes)
    {
        Label = label;
        Minutes = minutes;
    }

    public string Label { get; }
    public double Minutes { get; }
}

public sealed class TagSummaryPoint
{
    public TagSummaryPoint(string tagId, double totalMinutes, int count)
    {
        TagId = tagId;
        TotalMinutes = totalMinutes;
        Count = count;
    }

    public string TagId { get; }
    public double TotalMinutes { get; set; }
    public int Count { get; set; }
}

public sealed class AxisSummaryPoint
{
    public AxisSummaryPoint(string category, double totalMinutes, int count)
    {
        Category = category;
        TotalMinutes = totalMinutes;
        Count = count;
    }

    public string Category { get; }
    public double TotalMinutes { get; set; }
    public int Count { get; set; }
}

public sealed class StackedSeriesBucket
{
    public StackedSeriesBucket(string label, IReadOnlyDictionary<string, double> values)
    {
        Label = label;
        Values = values;
    }

    public string Label { get; }
    public IReadOnlyDictionary<string, double> Values { get; }
    public double TotalMinutes => Values.Values.Sum();
}

public enum AxisAggregationMode
{
    WorkType = 0,
    Client = 1,
    ClientWorkType = 2
}
