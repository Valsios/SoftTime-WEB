using FluentAssertions;

using SoftTime.Api.Responses;

namespace SoftTime.Tests;

public class ApiResponseTests
{
    [Fact]
    public void SuccessResult_WithData_IsSuccessfulAndCarriesTheData()
    {
        // Arrange
        var data = new[] { "Lundi", "Mardi" };

        // Act
        var sut = ApiResponse<string[]>.SuccessResult(data, "Planning chargé.");

        // Assert
        sut.Success.Should().BeTrue();
        sut.Message.Should().Be("Planning chargé.");
        sut.Data.Should().BeSameAs(data);
    }

    [Fact]
    public void SuccessResult_WithoutMessage_UsesOk()
    {
        // Act
        var sut = ApiResponse<int>.SuccessResult(42);

        // Assert
        sut.Message.Should().Be("OK");
    }

    [Fact]
    public void FailResult_WithMessage_IsUnsuccessfulWithoutData()
    {
        // Act
        var sut = ApiResponse<string>.FailResult("Période déjà validée.");

        // Assert
        sut.Success.Should().BeFalse();
        sut.Message.Should().Be("Période déjà validée.");
        sut.Data.Should().BeNull();
    }

    [Fact]
    public void SuccessResult_Always_StampsTheCurrentUtcTime()
    {
        // Act
        var sut = ApiResponse<int>.SuccessResult(1);

        // Assert
        sut.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        sut.Timestamp.Kind.Should().Be(DateTimeKind.Utc);
    }
}
