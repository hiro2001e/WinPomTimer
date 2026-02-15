namespace WinPomTimer.Domain;

public sealed class TaskTag
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#4B8BF4";
    public TagAxis Axis { get; set; } = TagAxis.WorkType;
    public bool IsArchived { get; set; }

    public TaskTag Clone()
    {
        return new TaskTag
        {
            Id = Id,
            Name = Name,
            ColorHex = ColorHex,
            Axis = Axis,
            IsArchived = IsArchived
        };
    }
}

public enum TagAxis
{
    WorkType = 0,
    Client = 1,
    Other = 2
}
