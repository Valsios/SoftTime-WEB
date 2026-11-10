namespace SoftTime.Infrastructure.Services.Punches;

internal static class ValidatedWeeks
{

    internal static DateTime Lundi(DateTime jour) => jour.Date.AddDays(-(((int)jour.DayOfWeek + 6) % 7));
}
