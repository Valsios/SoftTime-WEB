namespace SoftTime.Application.DTOs;

public record AuditConfigDto(int Id, string? Table, string? Column, string? Type, bool? Enabled, string? Base)
{
    public AuditConfigDto() : this(default, default, default, default, default, default) { }
}
