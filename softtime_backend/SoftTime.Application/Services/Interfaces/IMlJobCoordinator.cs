using System.Threading.Channels;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Contrat de <see cref="MlJobCoordinator"/> (convention §3.5 : les consommateurs dépendent de l'abstraction).</summary>
public interface IMlJobCoordinator
{
    ChannelReader<MlJobRequest> Reader { get; }

    MlJobState State { get; }

    bool TryEnqueue(MlJobRequest job);

    void SetRunning(string message);

    void SetIdle(string message, string? error = null);
}
