namespace SoftTime.Application.DTOs;

public sealed record JournalPageDto(IReadOnlyList<JournalEntryDto> Items, int Total, int Page, int PageSize);
