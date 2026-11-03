using System.Linq.Expressions;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Contrat de <see cref="JournalService"/> (convention §3.5 : les consommateurs dépendent de l'abstraction).</summary>
public interface IJournalService
{
    Task<JournalPageDto> ListAsync(JournalQuery query, CancellationToken ct = default);

    Task<JournalEntryDto> GetAsync(long id, CancellationToken ct = default);

    Task<JournalFacetsDto> GetFacetsAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
}
