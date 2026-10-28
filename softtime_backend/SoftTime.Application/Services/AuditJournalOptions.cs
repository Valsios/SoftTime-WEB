using Microsoft.Extensions.Configuration;

namespace SoftTime.Application.Services;

public sealed class AuditJournalOptions
{
    public const int DefaultDetailThreshold = 200;

    public bool Enabled { get; init; } = true;

    public int DetailThreshold { get; init; } = DefaultDetailThreshold;

    public static AuditJournalOptions From(IConfiguration configuration)
    {
        var rawThreshold = configuration["AuditJournal:DetailThreshold"];
        var threshold = int.TryParse(rawThreshold, out var configuredThreshold) && configuredThreshold > 0
            ? configuredThreshold
            : DefaultDetailThreshold;
        var enabled = !bool.TryParse(configuration["AuditJournal:Enabled"], out var configuredEnabled)
            || configuredEnabled;
        return new AuditJournalOptions { Enabled = enabled, DetailThreshold = threshold };
    }
}
