using FluentValidation;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using SoftTime.Application.Mapping;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddAutoMapper(typeof(MappingProfile).Assembly);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped<IPasswordHasher<Responsable>, PasswordHasher<Responsable>>();
        services.AddSingleton<EffectiveRightsCache>();

        services.AddOptions();

        // Clôture de paie assistée : réglages de la section « Closing » (valeurs par défaut en code),
        // refus du démarrage si un réglage est invalide ; analyse et présentation en lecture seule.
        services.AddOptions<ClosingOptions>().ValidateOnStart();
        services.AddSingleton<IConfigureOptions<ClosingOptions>, ClosingOptionsSetup>();
        services.AddSingleton<IValidateOptions<ClosingOptions>, ClosingOptionsValidator>();
        return services;
    }
}
