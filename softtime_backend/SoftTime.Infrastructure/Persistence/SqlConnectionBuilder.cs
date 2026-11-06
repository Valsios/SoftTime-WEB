using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence;

public static class SqlConnectionBuilder
{
    public static string FromSage(BddSage row)
        => ExternalConnectionFactory.Build(row.Serveur, row.NomBd, row.TypeAuth == true, row.TLogin, row.TMdp);

    public static string FromPointeuse(BddPointeuse row)
        => ExternalConnectionFactory.Build(row.Serveur, row.NomBd, row.TypeAuth == true, row.TLogin, row.TMdp);
}

