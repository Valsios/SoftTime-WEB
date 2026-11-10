namespace SoftTime.Application.Abstractions.Ml;

public sealed record EmployeeConfig(OvertimeWeekConfig Week, int? CategorieId, decimal? HeuresSemaine);
