using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Abstractions;

/// <summary>Accès à un serveur SQL externe : serveur, mode d'authentification et identifiants SQL.</summary>
public sealed record SqlServerCredentials(string? Serveur, bool SqlAuth, string? Login, string? Password)
{
    public static SqlServerCredentials Of(BddSage row) => new(row.Serveur, row.TypeAuth == true, row.TLogin, row.TMdp);

    public static SqlServerCredentials Of(BddPointeuse row) => new(row.Serveur, row.TypeAuth == true, row.TLogin, row.TMdp);
}
