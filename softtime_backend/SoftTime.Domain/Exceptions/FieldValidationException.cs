namespace SoftTime.Domain.Exceptions;

/// <summary>
/// Erreur de validation rattachée à un champ précis du formulaire.
/// Le middleware la traduit en HTTP 400 <c>{ error, field }</c> afin que l'interface
/// affiche le message sous le champ concerné.
/// </summary>
public sealed class FieldValidationException : DomainException
{
    public FieldValidationException(string field, string message)
        : base(message)
    {
        Field = field;
    }

    public string Field { get; }
}
