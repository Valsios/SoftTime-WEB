using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Services.Landing;

/// <summary>Données brutes d'un salarié sur la période ; une source absente vaut « aucune ligne ».</summary>
internal sealed record LandingEmployeeSources(
    List<HeureCorrigee>? Corrections,
    List<PlanningSalarie>? Plannings,
    List<LeaveDay>? Conges);
