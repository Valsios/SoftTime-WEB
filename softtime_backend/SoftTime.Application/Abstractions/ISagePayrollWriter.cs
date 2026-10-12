using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Application.Abstractions;

public interface ISagePayrollWriter
{
    Task SyncOvertimeAsync(string sageConnectionString, T_CST[] constants, int numSal, OvertimeSageCodes codes, decimal exo130, decimal exo150, decimal i130, decimal i150, decimal ferie, decimal nuit, decimal dim, CancellationToken cancellationToken = default);

    /// <summary>
    /// Écrit un cumul de paie dans <c>T_CUMSAL</c> pour un salarié et un code opérande donnés.
    /// Primitive partagée par les heures supplémentaires et par les cumuls cantine / frais.
    /// </summary>
    /// <returns>Le nombre de lignes mises à jour (0 si le cumul n'existe pas dans SAGE).</returns>
    Task<int> UpdateCumulAsync(string sageConnectionString, int numSal, short opCstCumul, decimal value, CancellationToken cancellationToken = default);
}
