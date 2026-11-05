using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Readers;

public class OtherPointeuseReader : IPointeuseReader
{
    private static readonly Regex SafeIdentifier = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    public bool CanHandle(T_BDD_POINTEUSE pteRow) => pteRow.TYPE_BASE == "AUTRE";

    private static void ValidateIdentifiers(params string?[] ids)
    {
        foreach (var id in ids)
            if (!string.IsNullOrWhiteSpace(id) && !SafeIdentifier.IsMatch(id))
                throw new InvalidOperationException($"Nom de table/colonne invalide dans le mapping pointeuse 'Autre' : {id}");
    }

    public async Task<IReadOnlyList<NormalizedPointeuseUser>> ListUsersAsync(T_BDD_POINTEUSE pteRow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pteRow.MAP_USER_TABLE) || string.IsNullOrWhiteSpace(pteRow.MAP_USER_COL_ID))
            throw new InvalidOperationException("Mapping incomplet pour la base pointeuse 'Autre' : table et colonne ID utilisateur requises.");

        ValidateIdentifiers(pteRow.MAP_USER_TABLE, pteRow.MAP_USER_COL_ID, pteRow.MAP_USER_COL_BADGE, pteRow.MAP_USER_COL_SSN, pteRow.MAP_USER_COL_NOM);

        var selId = $"[{pteRow.MAP_USER_COL_ID}]";
        var selBadge = pteRow.MAP_USER_COL_BADGE is { Length: > 0 } b ? $"[{b}]" : "NULL";
        var selSsn = pteRow.MAP_USER_COL_SSN is { Length: > 0 } s ? $"[{s}]" : "NULL";
        var selNom = pteRow.MAP_USER_COL_NOM is { Length: > 0 } n ? $"[{n}]" : "NULL";

        var sql = $"SELECT {selId} AS Id, {selBadge} AS Badge, {selSsn} AS Ssn, {selNom} AS Nom FROM [{pteRow.MAP_USER_TABLE}]";

        var connStr = ExternalConnectionFactory.Build(pteRow.SERVEUR, pteRow.NOM_BD, pteRow.TYPE_AUTH == true, pteRow.TLOGIN, pteRow.TMDP);
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
                    Badge: SqlReaderHelper.ReadText(reader, "Badge"),
                    Ssn: SqlReaderHelper.ReadText(reader, "Ssn"),
                    Nom: SqlReaderHelper.ReadText(reader, "Nom")));
        }
        return result;
    }

    public async Task<IReadOnlyList<NormalizedPunch>> ListPunchesAsync(
        T_BDD_POINTEUSE pteRow, DateTime from, DateTime to, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pteRow.MAP_PUNCH_TABLE) || string.IsNullOrWhiteSpace(pteRow.MAP_PUNCH_COL_USER_ID)
            || string.IsNullOrWhiteSpace(pteRow.MAP_PUNCH_COL_DATETIME))
            throw new InvalidOperationException("Mapping incomplet pour les pointages 'Autre' : table, colonne utilisateur et colonne date/heure requises.");

        ValidateIdentifiers(pteRow.MAP_PUNCH_TABLE, pteRow.MAP_PUNCH_COL_USER_ID, pteRow.MAP_PUNCH_COL_DATETIME, pteRow.MAP_PUNCH_COL_TYPE);

        var selUserId = $"[{pteRow.MAP_PUNCH_COL_USER_ID}]";
        var selDateHeure = $"[{pteRow.MAP_PUNCH_COL_DATETIME}]";
        var selType = pteRow.MAP_PUNCH_COL_TYPE is { Length: > 0 } t ? $"[{t}]" : "NULL";

        var sql = $"SELECT {selUserId} AS UserId, {selDateHeure} AS DateHeure, {selType} AS Type " +
                  $"FROM [{pteRow.MAP_PUNCH_TABLE}] WHERE {selDateHeure} >= @from AND {selDateHeure} <= @to";

        var connStr = ExternalConnectionFactory.Build(pteRow.SERVEUR, pteRow.NOM_BD, pteRow.TYPE_AUTH == true, pteRow.TLOGIN, pteRow.TMDP);
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
                        result.Add(new NormalizedPunch(UserId: userId.Trim(), DateHeure: dh, Type: SqlReaderHelper.ReadText(reader, "Type")));
        }
        return result;
    }
}