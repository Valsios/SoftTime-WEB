namespace SoftTime.Application.DTOs;

public record RoleChangePreviewRequest(decimal NewRoleId)
{
    public RoleChangePreviewRequest() : this(default(decimal)) { }
}
