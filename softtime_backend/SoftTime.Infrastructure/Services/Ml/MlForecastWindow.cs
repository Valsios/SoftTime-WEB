namespace SoftTime.Infrastructure.Services.Ml;

/// <summary>Fenêtre de prévision : absences jusqu'à <c>AbsenceTo</c>, semaines HS de <c>WeekStart</c> à <c>WeekEnd</c>.</summary>
internal sealed record MlForecastWindow(DateTime Today, DateTime AbsenceTo, DateTime WeekStart, DateTime WeekEnd);
