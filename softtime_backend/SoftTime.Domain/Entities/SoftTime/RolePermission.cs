namespace SoftTime.Domain.Entities.SoftTime;

public class RolePermission
{
    public int Id { get; set; }

    public decimal IdRole { get; set; }

    public string PermissionCode { get; set; } = string.Empty;

    public bool IsGranted { get; set; } = true;
}
