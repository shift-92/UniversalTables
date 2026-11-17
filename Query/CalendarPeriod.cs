namespace UniversalTables.Query;

// Inclusive boundaries also support the last day of year 9999 without overflow.
public sealed record CalendarPeriod(DateOnly Start, DateOnly End)
{
    public bool Contains(DateOnly date) => date >= Start && date <= End;
    public static DateOnly? Date(object? value) => value switch
    {
        DateOnly day => day,
        DateTime time => DateOnly.FromDateTime(time),
        _ => null
    };
}