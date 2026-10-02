namespace dentist_clinic_api.Services;

public class BookingSchedule
{
    public string TimeZoneId { get; }
    public TimeZoneInfo TimeZone { get; }
    public TimeOnly OpensAt { get; }
    public TimeOnly ClosesAt { get; }
    public int SlotIntervalMinutes { get; }
    public int DaysAhead { get; }
    public DayOfWeek[] WorkingDays { get; }

    public BookingSchedule(IConfiguration configuration)
    {
        var section = configuration.GetSection("Booking");
        TimeZoneId = section["TimeZone"] ?? "Pacific/Auckland";
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        OpensAt = TimeOnly.Parse(section["OpensAt"] ?? "09:00", System.Globalization.CultureInfo.InvariantCulture);
        ClosesAt = TimeOnly.Parse(section["ClosesAt"] ?? "17:00", System.Globalization.CultureInfo.InvariantCulture);
        SlotIntervalMinutes = section.GetValue("SlotIntervalMinutes", 15);
        DaysAhead = section.GetValue("DaysAhead", 90);
        WorkingDays = section.GetSection("WorkingDays").Get<DayOfWeek[]>() ??
            new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        if (OpensAt >= ClosesAt || SlotIntervalMinutes is < 5 or > 120 || DaysAhead is < 1 or > 365)
            throw new InvalidOperationException("Invalid clinic booking schedule.");
    }

    public DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZone).DateTime);
    public bool IsBookableDate(DateOnly date, DateTimeOffset now) =>
        date >= Today(now) && date <= Today(now).AddDays(DaysAhead);

    public IEnumerable<DateTime> Candidates(DateOnly date, int durationMinutes, DateTimeOffset now)
    {
        if (!IsBookableDate(date, now) || !WorkingDays.Contains(date.DayOfWeek) || durationMinutes <= 0)
            yield break;
        var start = date.ToDateTime(OpensAt, DateTimeKind.Unspecified);
        var close = date.ToDateTime(ClosesAt, DateTimeKind.Unspecified);
        for (; start.AddMinutes(durationMinutes) <= close; start = start.AddMinutes(SlotIntervalMinutes))
        {
            // Skip missing/repeated local times rather than guessing at a DST transition.
            if (TimeZone.IsInvalidTime(start) || TimeZone.IsAmbiguousTime(start)) continue;
            var utc = TimeZoneInfo.ConvertTimeToUtc(start, TimeZone);
            if (utc > now.UtcDateTime) yield return utc;
        }
    }
}
