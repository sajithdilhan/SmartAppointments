using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace SmartAppointments.BuildingBlocks.Web.OpenApi;

public static class OpenApiExtensions
{
    public const string BearerSecurityScheme = "Bearer";

    /// <summary>
    /// Registers the OpenAPI document with a JWT bearer security scheme, so that Scalar offers a
    /// box for the token Auth issues.
    /// </summary>
    public static IServiceCollection AddOpenApiWithBearerAuth(this IServiceCollection services)
    {
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
                    Description = "JWT bearer token issued by the Auth service. Enter the token without the 'Bearer' prefix."
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
}
