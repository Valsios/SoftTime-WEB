using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;

using SoftTime.Api.Middleware;

namespace SoftTime.Tests;

public class ExceptionHandlingMiddlewareTests
{
    private readonly Mock<ILogger<ExceptionHandlingMiddleware>> _logger = new();

    [Fact]
    public async Task InvokeAsync_UnhandledException_LogsTheErrorAndHidesItsMessage()
    {
        // Arrange
        var context = NewContext();
        var sut = new ExceptionHandlingMiddleware(_ => throw new TimeoutException("Server=prod;Password=secret"), _logger.Object);

        // Act
        await sut.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        (await ReadBodyAsync(context)).Should().NotContain("secret");
        _logger.Verify(
            l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<TimeoutException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_DomainException_IsNotLoggedAsAnError()
    {
        // Arrange
        var context = NewContext();
        var sut = new ExceptionHandlingMiddleware(_ => throw new ConflictException("Login déjà utilisé."), _logger.Object);

        // Act
        await sut.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        _logger.Verify(
            l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_RequestAbortedByTheClient_WritesNothing()
    {
        // Arrange
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var context = NewContext();
        context.RequestAborted = aborted.Token;
        var sut = new ExceptionHandlingMiddleware(
            ctx => Task.FromCanceled(ctx.RequestAborted), _logger.Object);

        // Act
        await sut.InvokeAsync(context);

        // Assert
        (await ReadBodyAsync(context)).Should().BeEmpty();
    }

    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await new StreamReader(context.Response.Body).ReadToEndAsync();
    }
}
