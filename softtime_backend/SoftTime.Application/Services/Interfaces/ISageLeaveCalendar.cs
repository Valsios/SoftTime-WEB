using SoftTime.Application.Abstractions;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Contrat de <see cref="SageLeaveCalendar"/> (convention §3.5 : les consommateurs dépendent de l'abstraction).</summary>
public interface ISageLeaveCalendar
{
    /// <summary>Congés de tous les salariés du tenant, par clé canonique de matricule.</summary>
    Task<Dictionary<string, List<LeaveDay>>> LoadAsync(CancellationToken ct = default);
}
