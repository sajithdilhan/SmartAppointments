using Booking.Application.Dependency;
using Booking.Infrastructure.Dependency;
using Scalar.AspNetCore;
using System.Text.Json.Serialization;
using SmartAppointments.BuildingBlocks.Web.Authentication;
using SmartAppointments.BuildingBlocks.Web.Middlewares;
using SmartAppointments.BuildingBlocks.Web.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApiWithBearerAuth();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorizationWithRoles();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Smart Appointments Booking API")
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
