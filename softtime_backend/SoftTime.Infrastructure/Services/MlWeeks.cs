namespace SoftTime.Infrastructure.Services;

internal static class MlWeeks
{

    internal static DateTime MondayOf(DateTime date)
        => date.Date.AddDays(-(((int)date.Date.DayOfWeek + 6) % 7));
}
