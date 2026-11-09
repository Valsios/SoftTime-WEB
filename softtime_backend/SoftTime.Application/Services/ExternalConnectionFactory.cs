using System.Data.Common;

using SoftTime.Application.Abstractions.DataSources;

namespace SoftTime.Application.Services;

/// <summary>
/// Fabrique sécurisée de chaînes de connexion SQL Server externes.
/// Utilise DbConnectionStringBuilder pour neutraliser les injections dans les paramètres de connexion
/// (points-virgules, guillemets, fragments de chaîne malveillants).
/// </summary>
public static class ExternalConnectionFactory
{
    public static string Build(
        SqlServerCredentials credentials, string? catalog)
    {
        var (server, sqlAuth, login, password) = credentials;
        var builder = new DbConnectionStringBuilder
        {
            ["Data Source"] = server ?? string.Empty,
            ["Initial Catalog"] = catalog ?? string.Empty,
            ["TrustServerCertificate"] = "True",
            ["Connect Timeout"] = "60"
        };

        if (!sqlAuth)
        {
            builder["Integrated Security"] = "True";
        }
        else
        {
            builder["User ID"] = login ?? string.Empty;
            builder["Password"] = password ?? string.Empty;
        }

        return builder.ConnectionString;
    }
}
