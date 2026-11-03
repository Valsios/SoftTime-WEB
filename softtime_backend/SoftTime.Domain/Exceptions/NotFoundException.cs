namespace SoftTime.Domain.Exceptions;

/// <summary>Ressource introuvable (HTTP 404).</summary>
public class NotFoundException : DomainException
{
    public const string DefaultMessage = "Élément introuvable.";

    public NotFoundException() : base(DefaultMessage) { }

    public NotFoundException(string message) : base(message) { }

    public NotFoundException(string entity, object id) : base($"{entity} avec l'identifiant {id} introuvable.") { }
}
