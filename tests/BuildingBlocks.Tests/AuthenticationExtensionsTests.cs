using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Web.Authentication;

namespace BuildingBlocks.Tests;

public class AuthenticationExtensionsTests
{
    private const string ValidKey = "0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData(null, "Jwt:SecretKey")]
    [InlineData("   ", "Jwt:SecretKey")]
    [InlineData("0123456789abcdef0123456789abcde", "at least 32 bytes")]
    public void A_Missing_Or_Short_Key_Stops_Startup(string? secretKey, string expectedMessage)
    {
        var configuration = Configuration(secretKey, "issuer", "audience");

        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddJwtAuthentication(configuration));

        Assert.Contains(expectedMessage, ex.Message);
    }

    [Theory]
    [InlineData("", "audience")]
    [InlineData("issuer", " ")]
    public void A_Blank_Issuer_Or_Audience_Stops_Startup(string issuer, string audience)
    {
        var configuration = Configuration(ValidKey, issuer, audience);

        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddJwtAuthentication(configuration));

        Assert.Contains("'Jwt:Issuer' and 'Jwt:Audience'", ex.Message);
    }

    [Fact]
    public void Tokens_Are_Validated_The_Way_Auth_Signs_Them()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddJwtAuthentication(Configuration(ValidKey, "issuer", "audience"));

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        var parameters = options.TokenValidationParameters;
        Assert.False(options.MapInboundClaims);
        Assert.True(parameters.ValidateIssuer);
        Assert.True(parameters.ValidateAudience);
        Assert.True(parameters.ValidateLifetime);
        Assert.True(parameters.ValidateIssuerSigningKey);
        Assert.Equal("issuer", parameters.ValidIssuer);
        Assert.Equal("audience", parameters.ValidAudience);
        Assert.Equal(Constants.RoleClaimType, parameters.RoleClaimType);
        Assert.Equal(TimeSpan.Zero, parameters.ClockSkew);
    }

    [Theory]
    [InlineData(Constants.AdminPolicy, new[] { Constants.AdminRole })]
    [InlineData(Constants.StaffPolicy, new[] { Constants.StaffRole })]
    [InlineData(Constants.CustomerPolicy, new[] { Constants.CustomerRole })]
    [InlineData(Constants.AdminOrStaffPolicy, new[] { Constants.AdminRole, Constants.StaffRole })]
    [InlineData(Constants.AllowedOriginsPolicy, new[] { Constants.AdminRole, Constants.StaffRole, Constants.CustomerRole })]
    public async Task Each_Policy_Requires_Its_Role_Set(string policyName, string[] roles)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddAuthorizationWithRoles();
        var provider = services.BuildServiceProvider().GetRequiredService<IAuthorizationPolicyProvider>();

        var policy = await provider.GetPolicyAsync(policyName);

        var requirement = Assert.Single(policy!.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Equal(roles, requirement.AllowedRoles);
    }

    private static IConfiguration Configuration(string? secretKey, string issuer, string audience) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SecretKey"] = secretKey,
                ["Jwt:Issuer"] = issuer,
                ["Jwt:Audience"] = audience
            })
            .Build();
}
