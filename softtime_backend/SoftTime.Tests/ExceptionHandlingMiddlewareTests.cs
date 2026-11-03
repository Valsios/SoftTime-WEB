using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SoftTime.Api.Middleware;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Application.Services;

namespace SoftTime.Tests;

/// <summary>Middleware global (convention §6.1.1) : une exception métier par code HTTP, rien de technique vers le client.</summary>
public class ExceptionHandlingMiddlewareTests
{
    public static TheoryData<Exception, int> MappedExceptions => new()
    {
        { new DomainException("Règle métier."), StatusCodes.Status400BadRequest },
        { new FieldValidationException("login", "Login requis."), StatusCodes.Status400BadRequest },
        { new NotFoundException(), StatusCodes.Status404NotFound },
        { new UnauthorizedException("Session expirée."), StatusCodes.Status401Unauthorized },
        { new ForbiddenException("Interdit."), StatusCodes.Status403Forbidden },
        { new ConflictException("Doublon."), StatusCodes.Status409Conflict },
    };

    [Theory]
    [MemberData(nameof(MappedExceptions))]
    public async Task InvokeAsync_DomainException_ReturnsItsStatusAndMessage(Exception exception, int expectedStatus)
    {
        // Act
        var (status, body) = await InvokeAsync(exception);

        // Assert
        Assert.Equal(expectedStatus, status);
        Assert.Equal(exception.Message, body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task InvokeAsync_FieldValidationException_ReturnsTheField()
    {
        // Act
        var (_, body) = await InvokeAsync(new FieldValidationException("login", "Login requis."));

        // Assert
        Assert.Equal("login", body.GetProperty("field").GetString());
    }

    [Fact]
    public async Task InvokeAsync_RoleChangeConfirmationRequired_ReturnsPreviewAsConflictBody()
    {
        // Arrange
        var preview = new RoleChangePreviewDto(new[] { "PROC:SHIFTS" }, 1, "agent");

        // Act
        var (status, body) = await InvokeAsync(new RoleChangeConfirmationRequiredException(preview));

        // Assert
        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Equal("agent", body.GetProperty("accountLogin").GetString());
        Assert.Equal(1, body.GetProperty("count").GetInt32());
    }

    [Theory]
    [InlineData("Sequence contains no elements")]
    [InlineData("Invalid object name 'dbo.T_SECRET' ; Password=FAKE")]
    public async Task InvokeAsync_TechnicalException_Returns500WithoutDetails(string technicalMessage)
    {
        // Act
        var (status, body) = await InvokeAsync(new InvalidOperationException(technicalMessage));

        // Assert
        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Equal(ExceptionHandlingMiddleware.InternalErrorMessage, body.GetProperty("error").GetString());
    }

    private static async Task<(int Status, JsonElement Body)> InvokeAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var sut = new ExceptionHandlingMiddleware(_ => throw exception, NullLogger<ExceptionHandlingMiddleware>.Instance);

        await sut.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, document.RootElement.Clone());
    }
}
