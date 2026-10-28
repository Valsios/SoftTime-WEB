using Microsoft.Extensions.Configuration;

namespace SoftTime.Application.Services;

public sealed class AuditJournalOptions
{
    public const int DefaultDetailThreshold = 200;

    public int DetailThreshold { get; init; } = DefaultDetailThreshold;

    public static AuditJournalOptions From(IConfiguration configuration)
    {
        var raw = configuration["AuditJournal:DetailThreshold"];
        return int.TryParse(raw, out var threshold) && threshold > 0
            ? new AuditJournalOptions { DetailThreshold = threshold }
            : new AuditJournalOptions();
    }
}
