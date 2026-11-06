namespace SoftTime.Application.Abstractions;

public sealed record MappedQuery(
    string? FilterColumn = null,
    MappedFilterKind FilterKind = MappedFilterKind.None,
    string? EqualsValue = null,
    string? RangeFrom = null,
    string? RangeTo = null,
    int? Top = null);
