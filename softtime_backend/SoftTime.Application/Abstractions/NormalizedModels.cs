// La fiche "Salarie"
public record NormalizedEmployee(
    string Matricule,
    string? Nom,
    string? Prenom,
    string? NumeroBadge,
    bool Desactive,
    string? CleInterne); // equivalent de SA_CompteurNumero ; peut etre absent (base "Autre")
 
// La fiche "Utilisateur pointeuse" (qui a quel badge)
public record NormalizedPointeuseUser(
    string Id,
    string? Badge,
    string? Ssn,
    string? Nom);
 
// La fiche "Pointage" (qui a pointe, a quelle heure)
public record NormalizedPunch(
    string UserId,
    DateTime DateHeure,
    string? Type);