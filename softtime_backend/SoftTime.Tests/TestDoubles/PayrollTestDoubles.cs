using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Domain.Models;
using SoftTime.Infrastructure.Integrations;

namespace SoftTime.Tests;

/// <summary>Marqueur d'envoi en mémoire : enregistre chaque appel, peut échouer à la demande.</summary>
internal sealed class RecordingSendLog : IPayrollSendLog
{
    private readonly List<string> _journal;

    public RecordingSendLog(List<string>? journal = null) => _journal = journal ?? new List<string>();

    public List<(int Id, PayrollSend Send, string Status, int Lines)> Envois { get; } = new();
    public List<(DateTime Debut, DateTime Fin)> Periodes { get; } = new();
    public bool EchecEnregistrement { get; set; }
    public bool EchecFin { get; set; }

    public Task<int> RecordAsync(PayrollSend send, CancellationToken ct = default)
    {
        _journal.Add("marqueur");
        if (EchecEnregistrement)
            throw new InvalidOperationException("T_PAIE_ENVOI indisponible");
        var id = Envois.Count + 1;
        Envois.Add((id, send, send.Status, send.Lines));
        return Task.FromResult(id);
    }

    public Task CompleteAsync(int id, string status, int lines, CancellationToken ct = default)
    {
        _journal.Add($"marqueur:{status}");
        if (EchecFin)
            throw new InvalidOperationException("T_PAIE_ENVOI indisponible");
        var index = Envois.FindIndex(e => e.Id == id);
        Envois[index] = Envois[index] with { Status = status, Lines = lines };
        return Task.CompletedTask;
    }

    /// <summary>Instantanés reçus, par identifiant d'envoi.</summary>
    public Dictionary<int, PayrollSnapshotDraft> Instantanes { get; } = new();
    public bool EchecInstantane { get; set; }

    public async Task<int> RecordWithSnapshotAsync(PayrollSend send, PayrollSnapshotDraft snapshot, CancellationToken ct = default)
    {
        if (EchecInstantane)
        {
            _journal.Add("marqueur");
            throw new InvalidOperationException("T_PAIE_INSTANTANE indisponible");
        }
        var id = await RecordAsync(send, ct);
        Instantanes[id] = snapshot;
        return id;
    }

    public async Task CompleteWithSnapshotAsync(int id, string status, int lines, PayrollSnapshotDraft snapshot, CancellationToken ct = default)
    {
        if (EchecInstantane)
        {
            _journal.Add($"marqueur:{status}");
            throw new InvalidOperationException("T_PAIE_INSTANTANE indisponible");
        }
        await CompleteAsync(id, status, lines, ct);
        Instantanes[id] = snapshot;
    }

    public Task RemoveAsync(int id, CancellationToken ct = default)
    {
        Envois.RemoveAll(e => e.Id == id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(DateTime Debut, DateTime Fin)>> ListPeriodsAsync(string sageDb, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<(DateTime Debut, DateTime Fin)>>(Periodes.ToList());
}

/// <summary>Service de variables qui renvoie une sélection fixe (par base, si donnée).</summary>
internal sealed class FixedVariableService(PayrollVariableSelection selection) : IPayrollVariableService
{
    public Task<PayrollVariableSelection> GetSelectionAsync(string sageDb, CancellationToken ct = default) => Task.FromResult(selection);
    public Task<Application.DTOs.PayrollVariablesActiveDto> GetActivesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Application.DTOs.PayrollVariablesConfigDto> GetConfigurationAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Application.DTOs.PayrollVariablesConfigDto> SaveAsync(IReadOnlyList<Application.DTOs.PayrollVariableSaveDto> variables, bool? remiseAZeroSansValeur = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Application.DTOs.PayrollVariablesConfigDto> SaveAsync(Application.DTOs.PayrollVariablesSaveRequest demande, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Application.DTOs.PayrollWriteTargetsDto> GetDestinationsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Application.DTOs.PayrollTargetColumnDto>> GetColonnesAsync(string? table, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Application.DTOs.PayrollWriteTargetsDto> SaveDestinationsAsync(Application.DTOs.PayrollWriteTargetsSaveDto destinations, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Application.DTOs.PayrollVariablesConfigDto> SaveDomainsAsync(Application.DTOs.PayrollDomainsDto domaines, CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>Schéma de base de paie en mémoire : tables et colonnes typées, ou erreur de lecture.</summary>
internal sealed class FakePayrollTargetSchema : IPayrollTargetSchema
{
    public Dictionary<string, List<PayrollTargetColumn>> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Illisible { get; set; }

    public Task<IReadOnlyList<string>> ListTablesAsync(CancellationToken ct = default) =>
        Illisible ? throw new SoftTime.Application.Abstractions.DataSources.ExternalConnectionException("serveur SQL injoignable (nom, instance ou réseau).", new InvalidOperationException())
                  : Task.FromResult<IReadOnlyList<string>>(Tables.Keys.ToList());

    public Task<IReadOnlyList<PayrollTargetColumn>> ListColumnsAsync(string table, CancellationToken ct = default) =>
        Illisible ? throw new SoftTime.Application.Abstractions.DataSources.ExternalConnectionException("serveur SQL injoignable (nom, instance ou réseau).", new InvalidOperationException())
                  : Task.FromResult<IReadOnlyList<PayrollTargetColumn>>(Tables.TryGetValue(table, out var c) ? c : new List<PayrollTargetColumn>());
}

/// <summary>Writer SAGE réel dont seule l'exécution SQL est remplacée : capture chaque mise à jour T_CUMSAL.</summary>
internal sealed class CapturingSagePayrollWriter : SagePayrollWriter
{
    private readonly Func<short, int> _lignes;
    private readonly int? _echecApres;

    /// <param name="lignes">Lignes modifiées par opérande (1 par défaut).</param>
    /// <param name="echecApres">Lève une erreur SQL après ce nombre de mises à jour réussies.</param>
    public CapturingSagePayrollWriter(Func<short, int>? lignes = null, int? echecApres = null)
    {
        _lignes = lignes ?? (_ => 1);
        _echecApres = echecApres;
    }

    public List<(int NumSal, short Operand, decimal Value)> Calls { get; } = new();

    public override Task<int> UpdateCumulAsync(string connectionString, int numSal, short opCst, decimal value, CancellationToken cancellationToken = default)
    {
        if (_echecApres is int n && Calls.Count >= n)
            throw new InvalidOperationException("Erreur SQL simulée");
        Calls.Add((numSal, opCst, value));
        return Task.FromResult(_lignes(opCst));
    }
}

/// <summary>Base AUTRE simulée : schéma, comptages et mises à jour enregistrés dans un journal ordonné.</summary>
internal sealed class FakeOtherPayrollDatabase : IOtherPayrollDatabase
{
    private readonly List<string> _journal;

    public FakeOtherPayrollDatabase(List<string> journal) => _journal = journal;

    public Dictionary<string, OtherPayrollColumn> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Func<string, string, int> Affected { get; set; } = (_, _) => 1;
    public bool EchecCommit { get; set; }
    public List<(string Sql, decimal Value, string Matricule)> Updates { get; } = new();
    public List<string> CountSql { get; } = new();

    public Task<IReadOnlyDictionary<string, OtherPayrollColumn>> ReadColumnsAsync(string schema, string table, CancellationToken ct)
    {
        _journal.Add($"colonnes:{schema}.{table}");
        return Task.FromResult<IReadOnlyDictionary<string, OtherPayrollColumn>>(Columns);
    }

    /// <summary>Lignes de paie trouvées par matricule (1 par défaut).</summary>
    public Func<string, long> LignesParMatricule { get; set; } = _ => 1L;

    public Task<long> CountAsync(string sql, string matricule, CancellationToken ct)
    {
        CountSql.Add(sql);
        return Task.FromResult(LignesParMatricule(matricule));
    }

    public Task BeginTransactionAsync(CancellationToken ct)
    {
        _journal.Add("begin");
        return Task.CompletedTask;
    }

    public Task<int> ExecuteUpdateAsync(string sql, decimal value, string matricule, CancellationToken ct)
    {
        _journal.Add("update");
        Updates.Add((sql, value, matricule));
        return Task.FromResult(Affected(sql, matricule));
    }

    public Task CommitAsync(CancellationToken ct)
    {
        _journal.Add("commit");
        return EchecCommit ? throw new InvalidOperationException("Commit refusé") : Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken ct)
    {
        _journal.Add("rollback");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
