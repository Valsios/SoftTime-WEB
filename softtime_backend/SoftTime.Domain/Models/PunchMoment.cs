namespace SoftTime.Domain.Models;

/// <summary>Pointage élémentaire (date + heure) utilisé par les règles d'appariement entrée/sortie.</summary>
public sealed record PunchMoment(DateTime Date, TimeSpan Heure);
