namespace SoftTime.Application.Abstractions;

/// <summary>Lecture des fichiers déposés pour un import (Excel, CSV) ; implémentée dans l'Infrastructure.</summary>
public interface IImportFileReader
{
    /// <summary>Lignes de la première feuille, indexées par en-tête de colonne.</summary>
    List<Dictionary<string, string>> ReadExcelRows(Stream stream);

    /// <summary>Lignes d'un CSV point-virgule (ou virgule), cellules nettoyées des espaces et guillemets.</summary>
    Task<IReadOnlyList<string[]>> ReadCsvLinesAsync(Stream stream, CancellationToken ct = default);
}
