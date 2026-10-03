using Availability.Application.Dependency;
using Availability.Infrastructure.Dependency;
using Availability.Infrastructure.Persistence;
using Scalar.AspNetCore;
using System.Text.Json.Serialization;
using SmartAppointments.BuildingBlocks.Web.Authentication;
using SmartAppointments.BuildingBlocks.Web.Middlewares;
using SmartAppointments.BuildingBlocks.Web.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
// DayOfWeek travels as "Monday" rather than 1.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApiWithBearerAuth();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddApiKeyAuthentication(builder.Configuration);
builder.Services.AddAuthorizationWithRoles();

var app = builder.Build();

await DatabaseMigrator.MigrateIfEnabledAsync(app.Services);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Smart Appointments Availability API")
               .WithTheme(ScalarTheme.DeepSpace)
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
               .EnableDarkMode();
        options.AddPreferredSecuritySchemes(["Bearer"]);
    });
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<LoggingMiddleware>();

// No HTTPS redirection: TLS terminates at the API gateway.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/healthz");

app.Run();
