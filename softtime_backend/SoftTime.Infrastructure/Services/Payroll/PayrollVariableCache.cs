using System.Collections.Concurrent;

namespace SoftTime.Infrastructure.Services.Payroll;

/// <summary>
/// Cache mémoire des sélections de variables par base SAGE (singleton). Sans expiration : l'enregistrement de la
/// configuration, seule écriture de T_PAIE_VARIABLE, retire l'entrée de la base concernée.
/// </summary>
public sealed class PayrollVariableCache
{
    private readonly ConcurrentDictionary<string, PayrollVariableSelection> _entrees = new(StringComparer.OrdinalIgnoreCase);

    internal bool TryGet(string sageDb, out PayrollVariableSelection selection) => _entrees.TryGetValue(Cle(sageDb), out selection!);

    internal void Set(string sageDb, PayrollVariableSelection selection) => _entrees[Cle(sageDb)] = selection;

    internal void Invalider(string sageDb) => _entrees.TryRemove(Cle(sageDb), out _);

    private static string Cle(string sageDb) => (sageDb ?? string.Empty).Trim();
}
