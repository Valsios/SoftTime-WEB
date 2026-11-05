using System.Collections.Generic;

namespace SoftTime.Domain.Models;

/// <summary>
/// Contrat commun de pagination côté backend.
/// </summary>
/// <typeparam name="T">Type des éléments de la page.</typeparam>
/// <param name="Items">Liste des éléments de la page courante.</param>
/// <param name="Total">Nombre total d'éléments correspondant aux critères.</param>
/// <param name="Page">Numéro de page courante (1-based).</param>
/// <param name="PageSize">Taille de la page appliquée.</param>
/// <param name="TotalPages">Nombre total de pages disponibles.</param>
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize, int TotalPages);
