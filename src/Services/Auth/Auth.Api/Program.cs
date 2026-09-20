using Auth.Api.Middlewares;
using Auth.Application.Dependency;
using Auth.Infrastructure.Dependency;
using Auth.Infrastructure.Persistence;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddAuthentication(builder.Configuration);
builder.Services.AddAuthorizationWithRoles();

var app = builder.Build();

await DatabaseSeeder.SeedAsync(app.Services);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Smart Appointments Auth API")
               .WithTheme(ScalarTheme.DeepSpace)
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
               .EnableDarkMode();
        options.AddPreferredSecuritySchemes(["Bearer"]);
    });
}

app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<LoggingMiddleware>();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/healthz");

app.Run();
