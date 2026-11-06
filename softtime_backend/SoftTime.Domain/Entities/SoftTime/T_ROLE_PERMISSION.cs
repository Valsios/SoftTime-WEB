namespace SoftTime.Domain.Entities.SoftTime;

public class T_ROLE_PERMISSION
{
    public int ID { get; set; }

    public decimal IDROLE { get; set; }

    public string PERMISSION_CODE { get; set; } = string.Empty;

    public bool IS_GRANTED { get; set; } = true;
}
