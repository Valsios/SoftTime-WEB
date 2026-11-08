using System.Text.RegularExpressions;

using Microsoft.Data.SqlClient;

using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Readers;

public class OtherPointeuseReader : IPointeuseReader
{
    private static readonly Regex SafeIdentifier = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    public bool CanHandle(BddPointeuse pteRow) => pteRow.TypeBase == "AUTRE";

    private static void ValidateIdentifiers(params string?[] ids)
    {
        foreach (var id in ids)
            if (!string.IsNullOrWhiteSpace(id) && !SafeIdentifier.IsMatch(id))
                throw new DomainException($"Nom de table/colonne invalide dans le mapping pointeuse 'Autre' : {id}");
    }

    public async Task<IReadOnlyList<NormalizedPointeuseUser>> ListUsersAsync(BddPointeuse pteRow, CancellationToken ct)
    {
        var sql = RequeteUtilisateurs(pteRow);

        var connStr = ExternalConnectionFactory.Build(SqlServerCredentials.Of(pteRow), pteRow.NomBd);
        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, con);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var result = new List<NormalizedPointeuseUser>();
        while (await reader.ReadAsync(ct))
        {
            var id = reader["Id"]?.ToString();
            if (string.IsNullOrWhiteSpace(id))
                continue;
                result.Add(new NormalizedPointeuseUser(
                    Id: id.Trim(),
                    Badge: SqlColumnReader.ReadText(reader, "Badge"),
                    Ssn: SqlColumnReader.ReadText(reader, "Ssn"),
                    Nom: SqlColumnReader.ReadText(reader, "Nom")));
        }
        return result;
    }

    /// <summary>Requête de lecture des utilisateurs d'après le mapping de la pointeuse, identifiants SQL contrôlés.</summary>
    private static string RequeteUtilisateurs(BddPointeuse pteRow)
    {
        if (string.IsNullOrWhiteSpace(pteRow.MapUserTable) || string.IsNullOrWhiteSpace(pteRow.MapUserColId))
            throw new DomainException("Mapping incomplet pour la base pointeuse 'Autre' : table et colonne ID utilisateur requises.");

        ValidateIdentifiers(pteRow.MapUserTable, pteRow.MapUserColId, pteRow.MapUserColBadge, pteRow.MapUserColSsn, pteRow.MapUserColNom);

        var selId = $"[{pteRow.MapUserColId}]";
        var selBadge = pteRow.MapUserColBadge is { Length: > 0 } b ? $"[{b}]" : "NULL";
        var selSsn = pteRow.MapUserColSsn is { Length: > 0 } s ? $"[{s}]" : "NULL";
        var selNom = pteRow.MapUserColNom is { Length: > 0 } n ? $"[{n}]" : "NULL";

        var sql = $"SELECT {selId} AS Id, {selBadge} AS Badge, {selSsn} AS Ssn, {selNom} AS Nom FROM [{pteRow.MapUserTable}]";
        return sql;
    }

    public async Task<IReadOnlyList<NormalizedPunch>> ListPunchesAsync(
        BddPointeuse pteRow, DateTime from, DateTime to, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pteRow.MapPunchTable) || string.IsNullOrWhiteSpace(pteRow.MapPunchColUserId)
            || string.IsNullOrWhiteSpace(pteRow.MapPunchColDatetime))
            throw new DomainException("Mapping incomplet pour les pointages 'Autre' : table, colonne utilisateur et colonne date/heure requises.");

        ValidateIdentifiers(pteRow.MapPunchTable, pteRow.MapPunchColUserId, pteRow.MapPunchColDatetime, pteRow.MapPunchColType);

        var selUserId = $"[{pteRow.MapPunchColUserId}]";
        var selDateHeure = $"[{pteRow.MapPunchColDatetime}]";
        var selType = pteRow.MapPunchColType is { Length: > 0 } t ? $"[{t}]" : "NULL";

        var sql = $"SELECT {selUserId} AS UserId, {selDateHeure} AS DateHeure, {selType} AS Type " +
                  $"FROM [{pteRow.MapPunchTable}] WHERE {selDateHeure} >= @from AND {selDateHeure} <= @to";

        var connStr = ExternalConnectionFactory.Build(SqlServerCredentials.Of(pteRow), pteRow.NomBd);
        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@from", from);
        cmd.Parameters.AddWithValue("@to", to);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var result = new List<NormalizedPunch>();
        while (await reader.ReadAsync(ct))
        {
            var userId = reader["UserId"]?.ToString();
            if (string.IsNullOrWhiteSpace(userId) || reader["DateHeure"] is not DateTime dh)
                continue;
                        result.Add(new NormalizedPunch(UserId: userId.Trim(), DateHeure: dh, Type: SqlColumnReader.ReadText(reader, "Type")));
        }
        return result;
    }
}