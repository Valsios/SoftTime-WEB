using Microsoft.Extensions.Options;

namespace SoftTime.Application.Security;

public sealed class AccountSecurityOptionsValidator : IValidateOptions<AccountSecurityOptions>
{
    public ValidateOptionsResult Validate(string? name, AccountSecurityOptions options)
    {
        var errors = new List<string>();
        if (options.RefreshTokenDays is < 1 or > 30)
            errors.Add("Security:RefreshTokenDays doit être compris entre 1 et 30.");
        if (options.MaxFailedLoginAttempts is < 1 or > 20)
            errors.Add("Security:MaxFailedLoginAttempts doit être compris entre 1 et 20.");
        if (options.LockoutMinutes is < 1 or > 1440)
            errors.Add("Security:LockoutMinutes doit être compris entre 1 et 1440.");
        if (options.AuthRequestsPerMinute < 1)
            errors.Add("Security:AuthRequestsPerMinute doit être positif.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
