namespace SoftTime.Infrastructure.MachineLearning;

/// <summary>Paramètres d'un modèle de classification binaire : label, département, features et taille des arbres.</summary>
internal sealed record MlNetBinarySpec(string Label, string Departement, string[] Features, int Leaves, int Trees);
