namespace SoftTime.Application.Abstractions.Authentication;

public interface ITokenService
{
    string CreateToken(TokenSubject subject);
}
