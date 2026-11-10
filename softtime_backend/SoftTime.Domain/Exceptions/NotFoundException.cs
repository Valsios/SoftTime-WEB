namespace SoftTime.Domain.Exceptions;

/// <summary>Ressource introuvable, traduite en HTTP 404.</summary>
public class NotFoundException : DomainException
{
    public NotFoundException(string message = "Ressource introuvable.")
        : base(message)
    {
    }

    public NotFoundException(string entity, object id)
        : base($"{entity} avec l'identifiant {id} introuvable.")
    {
    }
}
