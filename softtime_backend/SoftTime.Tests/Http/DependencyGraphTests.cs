using FluentAssertions;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

using SoftTime.Api.Controllers;

namespace SoftTime.Tests.Http;

/// <summary>
/// Chaque controller se construit à partir du conteneur : aucune dépendance manquante ni circulaire
/// (un cycle entre services n'apparaîtrait sinon qu'à la première requête sur la route concernée).
/// </summary>
public class DependencyGraphTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public DependencyGraphTests(ApiFactory factory) => _factory = factory;

    public static TheoryData<Type> Controllers()
    {
        var data = new TheoryData<Type>();
        foreach (var type in typeof(AuthController).Assembly.GetTypes()
                     .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
                     .OrderBy(t => t.Name))
            data.Add(type);
        return data;
    }

    [Theory]
    [MemberData(nameof(Controllers))]
    public void Controller_ResolvedFromTheContainer_IsCreated(Type controller)
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();

        // Act
        var sut = ActivatorUtilities.CreateInstance(scope.ServiceProvider, controller);

        // Assert
        sut.Should().NotBeNull();
    }
}
