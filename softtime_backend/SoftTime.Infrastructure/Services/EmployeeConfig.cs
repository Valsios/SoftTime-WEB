namespace SoftTime.Infrastructure.Services;

public sealed record EmployeeConfig(OvertimeWeekConfig Week, int? CategorieId, decimal? HeuresSemaine);
