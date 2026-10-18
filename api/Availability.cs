namespace ByteClub.Api;

/// <summary>Working hours, from appsettings "Availability". Gold Coast has no daylight saving.</summary>
public sealed class AvailabilityOptions
{
    public string TimeZone { get; set; } = "Australia/Brisbane";
    public List<DayOfWeek> Days { get; set; } = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];
    public TimeOnly DayStart { get; set; } = new(9, 0);
    public TimeOnly DayEnd { get; set; } = new(17, 0);
    public int SlotMinutes { get; set; } = 120;
    public int DaysAhead { get; set; } = 14;
    /// <summary>No same-hour surprises: the earliest bookable slot is this far from now.</summary>
    public int MinNoticeHours { get; set; } = 3;
}

public sealed record Slot(DateTimeOffset Start, DateTimeOffset End);

public sealed class Availability(AvailabilityOptions options, TimeProvider clock)
{
    private readonly TimeZoneInfo _tz = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);

    public IReadOnlyList<Slot> OpenSlots(IEnumerable<DateTimeOffset> taken)
    {
        var takenSet = taken.Select(t => t.ToUniversalTime()).ToHashSet();
        var now = clock.GetUtcNow();
        var earliest = now.AddHours(options.MinNoticeHours);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _tz).DateTime);

        var slots = new List<Slot>();
        for (var d = 0; d <= options.DaysAhead; d++)
        {
            var day = today.AddDays(d);
            if (!options.Days.Contains(day.DayOfWeek)) continue;
            for (var t = options.DayStart; t.AddMinutes(options.SlotMinutes) <= options.DayEnd && t >= options.DayStart;
                 t = t.AddMinutes(options.SlotMinutes))
            {
                var start = Local(day, t);
                if (start < earliest || takenSet.Contains(start.ToUniversalTime())) continue;
                slots.Add(new Slot(start, start.AddMinutes(options.SlotMinutes)));
                if (t.AddMinutes(options.SlotMinutes) < t) break; // wrapped past midnight
            }
        }
        return slots;
    }

    public bool IsOpen(DateTimeOffset start, IEnumerable<DateTimeOffset> taken) =>
        OpenSlots(taken).Any(s => s.Start.ToUniversalTime() == start.ToUniversalTime());

    private DateTimeOffset Local(DateOnly day, TimeOnly time)
    {
        var local = day.ToDateTime(time);
        return new DateTimeOffset(local, _tz.GetUtcOffset(local));
    }
}
