using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoftTime.Application.Serialization;

/// <summary>
/// Convertit les heures tolérant les deux formats réellement envoyés par le front :
/// « HH:mm » (valeur d'un &lt;input type="time"&gt;) et « HH:mm:ss » (format « c » attendu par
/// System.Text.Json). .NET 8 refuse « 08:00 » nativement, ce qui cassait toutes les saisies
/// d'heures (heures corrigées, pauses de catégorie, shifts, tolérances, planning…).
/// </summary>
public sealed class FlexibleTimeSpanConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
            return TimeSpan.Zero;
        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        throw new JsonException($"Format d'heure invalide : '{value}' (attendu HH:mm ou HH:mm:ss).");
    }

    /// <summary>Écriture au format « c », identique au comportement par défaut (gère les durées &gt; 24 h).</summary>
    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("c", CultureInfo.InvariantCulture));
}
