using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence;

public static class SqlConnectionBuilder
{
    public static string FromSage(T_BDD_SAGE row)
        => ExternalConnectionFactory.Build(row.SERVEUR, row.NOM_BD, row.TYPE_AUTH == true, row.TLOGIN, row.TMDP);

    public static string FromPointeuse(T_BDD_POINTEUSE row)
        => ExternalConnectionFactory.Build(row.SERVEUR, row.NOM_BD, row.TYPE_AUTH == true, row.TLOGIN, row.TMDP);
}

