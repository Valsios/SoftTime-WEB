using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Abstractions.SelfService;

/// <summary>
/// Identité résolue d'un salarié pour « Mon espace » : tout vient du jeton et de la base, jamais de la requête.
/// Contexte interne au serveur : il n'est jamais renvoyé au client.
/// </summary>
public sealed record SelfContext(
    decimal UserId, string SageDb, string Matricule, string Cle, IReadOnlyList<CardPaie> Cartes);
