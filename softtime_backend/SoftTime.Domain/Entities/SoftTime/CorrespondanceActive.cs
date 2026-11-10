namespace SoftTime.Domain.Entities.SoftTime;
public class CorrespondanceActive
{
    public int Id { get; set; }
    public bool? Active { get; set; }
    public string? SageServeur { get; set; }
    public string? SageBdd { get; set; }
    public string? PointeuseServeur { get; set; }
    public string? PointeuseBdd { get; set; }
    public DateTime? DerniereSync { get; set; }
    public DateTime? DateModif { get; set; }
}