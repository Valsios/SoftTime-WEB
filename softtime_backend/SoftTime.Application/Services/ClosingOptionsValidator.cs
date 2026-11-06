using Microsoft.Extensions.Options;

namespace SoftTime.Application.Services;

/// <summary>Refuse le démarrage quand un réglage « Closing » est invalide, avec un message clair.</summary>
public sealed class ClosingOptionsValidator : IValidateOptions<ClosingOptions>
{
    public ValidateOptionsResult Validate(string? name, ClosingOptions options)
    {
        var erreurs = options.Erreurs();
        return erreurs.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(erreurs.Select(e => "Réglage de clôture invalide : " + e));
    }
}
