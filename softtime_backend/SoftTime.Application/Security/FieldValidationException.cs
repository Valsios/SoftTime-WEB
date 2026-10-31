namespace SoftTime.Application.Security;

/// <summary>
/// Erreur de validation rattachée à un champ précis du formulaire.
/// Le middleware la traduit en HTTP 400 <c>{ error, field }</c> afin que l'interface
/// affiche le message sous le champ concerné. Dérive de
/// <see cref="InvalidOperationException"/> pour rester compatible avec les captures existantes.
/// </summary>
public sealed class FieldValidationException : InvalidOperationException
{
    public string Field { get; }

    public FieldValidationException(string field, string message) : base(message)
    {
        Field = field;
    }
}
