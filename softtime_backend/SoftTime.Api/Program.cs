using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SoftTime.Api.Middleware;
using SoftTime.Api.Security;
using SoftTime.Application;
using SoftTime.Application.Abstractions;
using SoftTime.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
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
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<ICompanyContext, CompanyContext>();
builder.Services.AddScoped<ICurrentUser>(sp =>
{
    var http = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
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

var jwtKey = builder.Configuration["Jwt:Key"] ?? "SoftTime_Dev_ChangeMe_32chars_minimum!!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantMiddleware>();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();
