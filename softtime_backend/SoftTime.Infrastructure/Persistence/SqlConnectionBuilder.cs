using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence;

public static class SqlConnectionBuilder
{
    public static string FromSage(BddSage row)
        => ExternalConnectionFactory.Build(SqlServerCredentials.Of(row), row.NomBd);

    public static string FromPointeuse(BddPointeuse row)
        => ExternalConnectionFactory.Build(SqlServerCredentials.Of(row), row.NomBd);
}

