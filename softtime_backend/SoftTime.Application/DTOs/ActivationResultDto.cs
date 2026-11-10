namespace SoftTime.Application.DTOs;

public record ActivationResultDto(bool HolidaysImported, int CardsAdded, int CardsDeactivated, string Message);
