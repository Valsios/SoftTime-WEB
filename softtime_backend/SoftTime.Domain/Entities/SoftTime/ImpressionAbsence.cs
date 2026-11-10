namespace SoftTime.Domain.Entities.SoftTime;

public class ImpressionAbsence
{
    public int Id { get; set; }

    public string? BddSage { get; set; }

    public string? BddPointeuse { get; set; }

    public string? Matricules { get; set; }

    public string? DatePointage { get; set; }

    public bool? AbsenceAm { get; set; }

    public bool? AbsencePm { get; set; }

    public string? IntituleAbs { get; set; }
}
