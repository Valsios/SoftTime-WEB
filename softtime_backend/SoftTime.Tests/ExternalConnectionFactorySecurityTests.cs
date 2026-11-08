using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;

namespace SoftTime.Tests;

public class ExternalConnectionFactorySecurityTests
{
    [Fact]
    public void Build_WithStandardSqlAuth_ReturnsValidConnectionString()
    {
        var cs = ExternalConnectionFactory.Build(new SqlServerCredentials("localhost", true, "sa", "P@ssword123"), "SoftTimeDb");

        cs.Should().Contain("Data Source=localhost");
        cs.Should().Contain("Initial Catalog=SoftTimeDb");
        cs.Should().Contain("User ID=sa");
        cs.Should().Contain("Password=P@ssword123");
        cs.Should().Contain("TrustServerCertificate=True");
        cs.Should().Contain("Connect Timeout=60");
    }

    [Fact]
    public void Build_WithIntegratedAuth_DoesNotIncludeCredentials()
    {
        var cs = ExternalConnectionFactory.Build(new SqlServerCredentials("localhost", false, "sa", "secret"), "SoftTimeDb");

        cs.Should().Contain("Data Source=localhost");
        cs.Should().Contain("Initial Catalog=SoftTimeDb");
        cs.Should().Contain("Integrated Security=True");
        cs.Should().NotContain("User ID");
        cs.Should().NotContain("Password");
    }

    [Theory]
    [InlineData("my;server", "test;Initial Catalog=evil;")]
    [InlineData("srv", "pass;Integrated Security=True;")]
    [InlineData("srv\"--", "pwd\"'")]
    public void Build_WithInjectionPayloads_ProperlyQuotesAndEscapesValues(string server, string password)
    {
        var cs = ExternalConnectionFactory.Build(new SqlServerCredentials(server, true, "admin", password), "DbTest");

        // DbConnectionStringBuilder must safely encapsulate semicolons and special characters
        cs.Should().NotBeNull();

        var builder = new System.Data.Common.DbConnectionStringBuilder
        {
            ConnectionString = cs
        };

        // When parsed back, the values must match exactly what was passed in, without injecting new keys
        builder["Data Source"].Should().Be(server);
        builder["Initial Catalog"].Should().Be("DbTest");
        builder["User ID"].Should().Be("admin");
        builder["Password"].Should().Be(password);
    }
}
