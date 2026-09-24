using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_SOURCE_CONFIG")]
public class T_SOURCE_CONFIG
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public string Mode { get; set; } = "SAGE";
    public string? TableName { get; set; }
    public string? ColMatricule { get; set; }
    public string? ColDepartement { get; set; }
    public string? ColService { get; set; }
    public string? ColCodeDepartement { get; set; }
    public string? ExtServeur { get; set; }
    public string? ExtBase { get; set; }
    public string? ExtLogin { get; set; }
    public string? ExtPassword { get; set; }
    public bool ExtSqlAuth { get; set; } = true;
}