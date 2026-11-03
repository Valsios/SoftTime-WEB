namespace SoftTime.Application.DTOs;

/// <summary>Fichier produit par un service (export, modèle d'import) : le contrôleur le renvoie tel quel.</summary>
public sealed record GeneratedFile(byte[] Content, string ContentType, string FileName)
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}
