using AutoMapper;

using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests;

/// <summary>
/// La fiche de correspondance paie pilote désormais le département. Le mapping doit alimenter
/// <c>T_CARDPAIE.DEPARTEMENT</c> lors d'un enregistrement.
/// </summary>
public class CardPaieMappingTests
{
    [Fact]
    public void Map_DtoWithDepartement_SetsCardPaieDepartement()
    {
        // Arrange
        var sut = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
        var dto = new CardPaieDto(0, "001", "Dupont", "Jean", null, null, null, null, null, null, null)
        {
            Departement = "INFO",
        };

        // Act
        var entity = sut.Map<CardPaie>(dto);

        // Assert
        entity.Departement.Should().Be("INFO");
    }
}
