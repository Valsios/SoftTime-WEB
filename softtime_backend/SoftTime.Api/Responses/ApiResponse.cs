using System.Text.Json.Serialization;

namespace SoftTime.Api.Responses;

/// <summary>
/// Enveloppe standard des réponses de l'API (§7.3) : indicateur de succès, message lisible, données et horodatage UTC.
/// En échec, <see cref="Field"/> désigne le champ du formulaire concerné (règle métier ou validation) et
/// <see cref="Errors"/> détaille les erreurs de validation par champ.
/// </summary>
/// <typeparam name="TData">Type des données renvoyées.</typeparam>
public sealed record ApiResponse<TData>
{
    private const string DefaultSuccessMessage = "OK";

    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public TData? Data { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Field { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }

    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public static ApiResponse<TData> SuccessResult(TData data, string message = DefaultSuccessMessage)
        => new() { Success = true, Message = message, Data = data };

    public static ApiResponse<TData> FailResult(string message)
        => new() { Success = false, Message = message };
}
