using System.Threading.Channels;

namespace SoftTime.Application.Services;

/// <summary>Demande de traitement ML, exécutée par le service d'arrière-plan.</summary>
public sealed record MlJobRequest(string? BddSage, bool FullRefresh, bool Train);

/// <summary>État exposé par <c>GET /api/predictions/status</c>.</summary>
public sealed record MlJobState(
    bool Running,
    string Message,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    string? LastError);

/// <summary>
/// File d'attente bornée à un job. Les endpoints admin enfilent et répondent 202 ; le service
/// planifié consomme. Cela évite les requêtes HTTP longues et les doubles exécutions.
/// </summary>
public sealed class MlJobCoordinator
{
    private readonly Channel<MlJobRequest> _channel =
        Channel.CreateBounded<MlJobRequest>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

    private MlJobState _state = new(false, "Inactif", null, null, null);

    public ChannelReader<MlJobRequest> Reader => _channel.Reader;

    public MlJobState State => Volatile.Read(ref _state);

    public bool TryEnqueue(MlJobRequest job) => _channel.Writer.TryWrite(job);

    public void SetRunning(string message)
        => Volatile.Write(ref _state, new MlJobState(true, message, DateTime.Now, null, null));

    public void SetIdle(string message, string? error = null)
        => Volatile.Write(ref _state, new MlJobState(false, message, null, DateTime.Now, error));
}
