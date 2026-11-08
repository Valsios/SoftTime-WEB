namespace SoftTime.Application.Abstractions;

public interface ITokenService
{
    string CreateToken(TokenSubject subject);
}
