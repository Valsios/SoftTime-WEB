namespace SoftTime.Application.DTOs;

public record AuditEventDto(int Id, string? Table, string? Column, string? User, string? Action, DateTime? Date, string? NewValue, string? OldValue, string? Base)
{
    public AuditEventDto() : this(default, default, default, default, default, default, default, default, default) { }
}
