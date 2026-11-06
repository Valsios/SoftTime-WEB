namespace SoftTime.Application.DTOs;

public record OvertimeSageCodes(string Exo130, string Exo150, string I130, string I150, string Ferie, string Dim, string Nuit)
{
    public OvertimeSageCodes() : this("HS01", "HS02", "HS03", "HS04", "HS05", "HS06", "HS07") { }
}
