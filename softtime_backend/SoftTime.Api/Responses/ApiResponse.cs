namespace SoftTime.Api.Responses;

/// <summary>
/// Enveloppe standard des réponses de l'API (§7.3) : indicateur de succès, message lisible, données et horodatage UTC.
/// </summary>
/// <typeparam name="TData">Type des données renvoyées.</typeparam>
public sealed class ApiResponse<TData>
{
    private const string DefaultSuccessMessage = "OK";

    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public TData? Data { get; init; }

    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public static ApiResponse<TData> SuccessResult(TData data, string message = DefaultSuccessMessage)
        => new() { Success = true, Message = message, Data = data };

    public static ApiResponse<TData> FailResult(string message)
        => new() { Success = false, Message = message };
}
