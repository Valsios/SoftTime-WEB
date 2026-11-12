using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using SoftTime.Application.Abstractions.Authentication;

namespace SoftTime.Api.Security;

public class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    public JwtTokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public string CreateToken(TokenSubject subject)
    {
        var (userId, login, roleId, matricule, rights) = subject;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
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
            new("rights", string.Join(",", rights)),
            new(PermissionClaims.Permissions, PermissionClaims.Join(subject.Permissions)),
            new(PermissionClaims.RightsStamp, subject.RightsStamp)
        };
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
