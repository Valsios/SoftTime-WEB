using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Services.PlanningImport;

/// <summary>Valeurs d'une journée de planning importée.</summary>
internal readonly record struct PlanningDayValues(int? NoShift, DateTime? Ha, DateTime? Hd, TimeSpan Pause, bool Off)
{
    /// <summary>Jour de repos : aucune heure, pause nulle.</summary>
    public static PlanningDayValues Repos => new(null, null, null, TimeSpan.Zero, true);

    public void Appliquer(PlanningSalarie planning, string sageDb, string pointeuseDb)
    {
        planning.NoShift = NoShift;
        planning.Ha = Ha;
        planning.Hd = Hd;
        planning.Pause = Pause;
        planning.OffP = Off;
        planning.BddSage = sageDb;
        planning.BddPointeuse = pointeuseDb;
    }
}
