using AutoMapper;

using FluentAssertions;

using SoftTime.Application.Mapping;

namespace SoftTime.Tests;

/// <summary>Chaque membre de destination du profil est mappé : un renommage d'entité ou de DTO casse ce test, pas la prod.</summary>
public class MappingProfileTests
{
    [Fact]
    public void MappingProfile_Configuration_IsValid()
    {
        // Arrange
        var sut = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());

        // Act
        var act = () => sut.AssertConfigurationIsValid();

        // Assert
        act.Should().NotThrow();
    }
}
