namespace SoftTime.Application.DTOs;

public record LoginRequest(string Login, string Password)
{
    public LoginRequest() : this(default!, default!) { }
}
