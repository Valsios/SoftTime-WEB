namespace SoftTime.Application.Security;

/// <summary>Une base déclarée : son identifiant (porté par les droits) et son nom (porté par les en-têtes de requête).</summary>
public sealed record DatabaseRef(int Id, string Name);
