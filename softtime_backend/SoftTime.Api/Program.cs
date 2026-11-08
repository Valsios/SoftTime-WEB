using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

using SoftTime.Api.HostedServices;
using SoftTime.Api.Middleware;
using SoftTime.Api.Security;
using SoftTime.Application;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Security;
using SoftTime.Application.Serialization;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure;
using SoftTime.Infrastructure.SchemaMigrations;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(o =>
    {
        o.Filters.Add<SoftTime.Api.Validation.FluentValidationFilter>();
        o.Filters.Add<SoftTime.Api.Responses.ApiResponseResultFilter>();
    })
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory =
        context => SoftTime.Api.Responses.ApiErrors.FromModelState(context.ModelState))
    .AddJsonOptions(o => SoftTimeJsonOptions.Configure(o.JsonSerializerOptions));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Soft Time API",
        Version = "v1",
        Description = "GTA Soft Time. JWT + en-têtes X-Sage-Database / X-Pointeuse-Database. Catalogue Angular: GET /api/meta/endpoints"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Authorization: Bearer {token}"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        },
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "SageDb" } },
            Array.Empty<string>()
        },
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "PointeuseDb" } },
            Array.Empty<string>()
        }
    });
    c.AddSecurityDefinition("SageDb", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-Sage-Database",
        Description = "Nom de la base SAGE (T_BDD_SAGE.NOM_BD)"
    });
    c.AddSecurityDefinition("PointeuseDb", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-Pointeuse-Database",
        Description = "Nom de la base pointeuse (optionnel si une est ACTIVE)"
    });
    c.DocInclusionPredicate((_, _) => true);
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<MlOptions>(builder.Configuration.GetSection("Ml"));
builder.Services.AddHostedService<MlMaintenanceHostedService>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<ICompanyContext, CompanyContext>();
builder.Services.AddScoped<SystemExecution>();
builder.Services.AddScoped<ICurrentUser>(sp =>
{
    var http = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
    // Scope d'arrière-plan (jobs ML) : identité système administratrice, jamais pour une requête HTTP.
    if (http is null && sp.GetRequiredService<SystemExecution>().Active)
        return new CurrentUser { Login = SystemExecution.Login, RoleId = SoftTime.Domain.Services.AdminRules.AdminRoleId };
    var principal = http?.User;
    if (principal?.Identity?.IsAuthenticated != true)
        return new CurrentUser();
    var rights = (principal.FindFirst("rights")?.Value ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(v => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (int?)null)
        .Where(n => n.HasValue)
        .Select(n => n!.Value)
        .ToList();
    var idRaw = principal.FindFirstValue("uid")
               ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
    decimal.TryParse(idRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var uid);
    decimal.TryParse(principal.FindFirstValue("roleId"), NumberStyles.Any, CultureInfo.InvariantCulture, out var role);
    return new CurrentUser
    {
        UserId = uid == 0 ? null : uid,
        Login = principal.Identity?.Name,
        RoleId = role == 0 ? null : role,
        Matricule = principal.FindFirstValue("matricule"),
        Rights = rights
    };
});

// Secrets hors du dépôt : user-secrets en développement, variables d'environnement ailleurs.
if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("SoftTime")))
    throw new InvalidOperationException(
        "Chaîne de connexion absente : définissez ConnectionStrings:SoftTime avec « dotnet user-secrets set \"ConnectionStrings:SoftTime\" \"<valeur>\" » "
        + "en développement, ou la variable d'environnement ConnectionStrings__SoftTime.");

builder.Services.AddOptions<JwtOptions>().Bind(builder.Configuration.GetSection(JwtOptions.Section)).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
builder.Services.AddOptions<AccountSecurityOptions>().Bind(builder.Configuration.GetSection(AccountSecurityOptions.Section)).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<AccountSecurityOptions>, AccountSecurityOptionsValidator>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwt) =>
    {
        options.MapInboundClaims = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.Key)),
            // Aucune marge : un jeton d'accès expiré est refusé à la seconde près (le client le rafraîchit).
            ClockSkew = TimeSpan.Zero,
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    });
builder.Services.AddAuthorization();

// Origines explicites seulement (Cors:AllowedOrigins) ; liste vide : appels de même origine uniquement.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (context, ct) => new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
        SoftTime.Api.Responses.ApiErrors.Fail("Trop de tentatives. Patientez une minute avant de réessayer."), ct));
    o.AddPolicy(AuthRateLimit.Policy, context =>
    {
        var permits = context.RequestServices.GetRequiredService<IOptions<AccountSecurityOptions>>().Value.AuthRequestsPerMinute;
        return RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "inconnue",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });
});

var app = builder.Build();
// Clé JWT et réglages de sécurité contrôlés avant toute migration : message clair plutôt qu'un échec à la première requête.
_ = app.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
_ = app.Services.GetRequiredService<IOptions<AccountSecurityOptions>>().Value;
using (var scope = app.Services.CreateScope())
{
    // Scripts SQL versionnés (migrations/versions) : uniquement si Database:AutoMigrate = true (faux par défaut),
    // AVANT les Ensure* ci-dessous. Un échec arrête le démarrage avec un message explicite.
    await scope.ServiceProvider.GetRequiredService<SchemaMigrator>().RunIfEnabledAsync(CancellationToken.None);

    await SageDatabaseConfiguration.EnsureStorageAsync(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), CancellationToken.None);
    await PunchProcessingConfiguration.EnsureStorageAsync(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), CancellationToken.None);
    await HistoryArchiveConfiguration.EnsureStorageAsync(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), CancellationToken.None);
    // Le journal central est créé par la migration 003 ; sans elle, le démarrage s'arrête avec un message clair.
    if (scope.ServiceProvider.GetRequiredService<IOptions<AuditJournalOptions>>().Value.Enabled)
        await SchemaPresenceGuard.EnsureAuditJournalAsync(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), CancellationToken.None);
    await UserAccountConfiguration.EnsureStorageAsync(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), CancellationToken.None);
    // Jetons de rafraîchissement et verrouillage des comptes : migration 004, sinon arrêt avec un message clair.
    await SchemaPresenceGuard.EnsureAccountSecurityAsync(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), CancellationToken.None);
    await RolePermissionConfiguration.EnsureStorageAsync(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), CancellationToken.None);

    // Base analytique du moteur prédictif : applique les migrations EF versionnées (schéma ml).
    // Un échec n'empêche pas l'API de démarrer : la fonctionnalité reste simplement indisponible.
    try
    {
        await MlStorageConfiguration.EnsureSchemaAsync(app.Services, CancellationToken.None);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Impossible d'appliquer les migrations du moteur prédictif (schéma ml).");
    }

    // Migration globale contrôlée des mots de passe en clair :
    // Par défaut, la migration est assurée de manière fluide et isolée à la connexion de chaque utilisateur (AuthService.LoginAsync),
    // éliminant tout risque de verrou concurrent (multi-instances / conteneurs) ou de dépassement de délai au démarrage.
    // L'exécution batch au démarrage reste activable explicitement via « Security:MigrateLegacyPasswordsOnStartup » (AccountSecurityOptions) :
    if (app.Services.GetRequiredService<IOptions<AccountSecurityOptions>>().Value.MigrateLegacyPasswordsOnStartup)
    {
        await UserAccountConfiguration.MigrateLegacyPasswordsAsync(
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            scope.ServiceProvider.GetRequiredService<IPasswordHasher<Responsable>>(),
            CancellationToken.None);
    }
}
// Ordre de la convention (§5.7) : erreurs, HSTS, HTTPS, CORS, authentification, autorisation, limitation.
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseMiddleware<SecurityHeadersMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<TenantMiddleware>();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();
