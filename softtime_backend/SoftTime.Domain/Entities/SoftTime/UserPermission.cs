namespace SoftTime.Domain.Entities.SoftTime;

public class UserPermission
{
    public int Id { get; set; }

    public decimal IdResponsable { get; set; }

    public string PermissionCode { get; set; } = string.Empty;

    public bool IsGranted { get; set; } = true;
}
