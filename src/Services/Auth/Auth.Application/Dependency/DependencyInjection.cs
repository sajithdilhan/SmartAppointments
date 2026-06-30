using Auth.Application.Commands;
using Auth.Application.Models;
using Auth.Application.Validations;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using SmartAppointments.BuildingBlocks;
using System.Security.Claims;
using System.Text;

namespace Auth.Application.Dependency;

public static class DependencyInjection
{
    private const string BearerSecurityScheme = "Bearer";
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Register application services here
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddScoped<IValidator<RegisterCustomerCommand>, RegisterCustomerCommandValidator>();
        services.AddScoped<IValidator<LoginUserCommand>, LoginUserRequestValidator>();

        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[BearerSecurityScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "JWT bearer token authentication. Enter the token without the 'Bearer' prefix."
                };

                document.Security ??= [];
                document.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(BearerSecurityScheme, document)] = []
                });

                return Task.CompletedTask;
            });
        });
        return services;
    }

    public static IServiceCollection AddAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var authSettings = configuration.GetSection("Jwt").Get<JwtOptions>();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = authSettings!.Issuer,
                ValidAudience = authSettings!.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authSettings!.SecretKey)),
                NameClaimType = ClaimTypes.Name,
                RoleClaimType = Constants.RoleClaimType,
                ClockSkew = TimeSpan.Zero
            };
        });
        return services;
    }

    public static IServiceCollection AddAuthorizationWithRoles(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Constants.AdminPolicy, policy => policy.RequireRole(Constants.AdminRole))
            .AddPolicy(Constants.StaffPolicy, policy => policy.RequireRole(Constants.StaffRole))
            .AddPolicy(Constants.AdminOrStaffPolicy, policy => policy.RequireRole(Constants.AdminRole, Constants.StaffRole))
            .AddPolicy(Constants.CustomerPolicy, policy => policy.RequireRole(Constants.CustomerRole))
            .AddPolicy(Constants.AllowedOriginsPolicy, policy => policy.RequireRole(Constants.AdminRole, Constants.StaffRole, Constants.CustomerRole));
        return services;
    }
}
