using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SoftTime.Application.Abstractions;

namespace SoftTime.Api.Security;

public class JwtTokenService : ITokenService
{
    private readonly IConfiguration _configuration;
    public JwtTokenService(IConfiguration configuration) => _configuration = configuration;

    public string CreateToken(decimal userId, string login, decimal roleId, string? matricule, IEnumerable<int> rights)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
        var uid = ((long)userId).ToString(CultureInfo.InvariantCulture);
        var rid = ((long)roleId).ToString(CultureInfo.InvariantCulture);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, uid),
            new(ClaimTypes.NameIdentifier, uid),
            new("uid", uid),
            new(ClaimTypes.Name, login),
            new("roleId", rid),
            new("matricule", matricule ?? string.Empty),
            new("rights", string.Join(",", rights))
        };
        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
