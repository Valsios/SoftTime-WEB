using SoftTime.Application.Services;

namespace SoftTime.Tests;

public class ExternalConnectionFactorySecurityTests
{
    [Fact]
    public void Build_WithStandardSqlAuth_ReturnsValidConnectionString()
    {
        var cs = ExternalConnectionFactory.Build("localhost", "SoftTimeDb", true, "sa", "P@ssword123");

        Assert.Contains("Data Source=localhost", cs);
        Assert.Contains("Initial Catalog=SoftTimeDb", cs);
        Assert.Contains("User ID=sa", cs);
        Assert.Contains("Password=P@ssword123", cs);
        Assert.Contains("TrustServerCertificate=True", cs);
        Assert.Contains("Connect Timeout=60", cs);
    }

    [Fact]
    public void Build_WithIntegratedAuth_DoesNotIncludeCredentials()
    {
        var cs = ExternalConnectionFactory.Build("localhost", "SoftTimeDb", false, "sa", "secret");

        Assert.Contains("Data Source=localhost", cs);
        Assert.Contains("Initial Catalog=SoftTimeDb", cs);
        Assert.Contains("Integrated Security=True", cs);
        Assert.DoesNotContain("User ID", cs);
        Assert.DoesNotContain("Password", cs);
    }

    [Theory]
    [InlineData("my;server", "test;Initial Catalog=evil;")]
    [InlineData("srv", "pass;Integrated Security=True;")]
    [InlineData("srv\"--", "pwd\"'")]
    public void Build_WithInjectionPayloads_ProperlyQuotesAndEscapesValues(string server, string password)
    {
        var cs = ExternalConnectionFactory.Build(server, "DbTest", true, "admin", password);

        // DbConnectionStringBuilder must safely encapsulate semicolons and special characters
        Assert.NotNull(cs);

        var builder = new System.Data.Common.DbConnectionStringBuilder
        {
            ConnectionString = cs
        };

        // When parsed back, the values must match exactly what was passed in, without injecting new keys
        Assert.Equal(server, builder["Data Source"]);
        Assert.Equal("DbTest", builder["Initial Catalog"]);
        Assert.Equal("admin", builder["User ID"]);
        Assert.Equal(password, builder["Password"]);
    }
}
