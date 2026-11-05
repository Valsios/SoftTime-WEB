using System.Text.Json;

namespace SoftTime.Application.Serialization;

/// <summary>
/// Options JSON partagées par l'API et les tests, pour qu'un test de contrat ne puisse pas
/// diverger de la configuration réelle de l'application.
/// </summary>
public static class SoftTimeJsonOptions
{
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNameCaseInsensitive = true;
        options.Converters.Add(new SoftTime.Application.Serialization.FlexibleTimeSpanConverter());
    }
}
