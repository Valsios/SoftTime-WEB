using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Closing;

/// <summary>Cache mémoire de la checklist de clôture, partagé par le processus (une entrée par clé de
/// demande).</summary>
public interface IClosingChecklistCache
{
    bool TryGet(string cle, DateTimeOffset maintenant, out ClosingChecklistDto valeur);

    void Set(string cle, ClosingChecklistDto valeur, DateTimeOffset maintenant, TimeSpan duree);
}
