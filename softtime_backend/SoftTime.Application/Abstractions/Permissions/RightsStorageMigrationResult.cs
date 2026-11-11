namespace SoftTime.Application.Abstractions.Permissions;

/// <summary>Bilan de la migration des droits : déjà faite, ou nombre de rôles et de comptes convertis.</summary>
public sealed record RightsStorageMigrationResult(bool AlreadyApplied, int ConvertedRoles, int ConvertedUsers);
