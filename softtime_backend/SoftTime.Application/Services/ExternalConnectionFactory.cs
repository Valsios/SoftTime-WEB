namespace SoftTime.Application.Services;

public static class ExternalConnectionFactory
{
    public static string Build(string? server, string? catalog, bool sqlAuth, string? login, string? password)
    {
        if (!sqlAuth)
            return $"Data Source={server};Initial Catalog={catalog};Integrated Security=True;TrustServerCertificate=True;Connect Timeout=60";
        return $"Data Source={server};Initial Catalog={catalog};User ID={login};Password={password};TrustServerCertificate=True;Connect Timeout=60";
    }
}
