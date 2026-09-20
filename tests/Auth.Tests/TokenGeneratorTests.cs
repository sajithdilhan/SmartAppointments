using Auth.Application.Models;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Enums;
using System.IdentityModel.Tokens.Jwt;

namespace Auth.Tests;

public class TokenGeneratorTests
{
    private const string SecretKey = "4pz1K0PoZoBUbjWxk3EmfK-C8CTLMQxExjhgHlMgM97F7qPTkdbhFSusdEgLJaxSrNcSEjafS9hlm3XeCqqV8jI";

    [Fact]
    public void GenerateAccessToken_Emits_Short_Claim_Names()
    {
        // The JwtBearer handler is configured with MapInboundClaims = false, so the claim names
        // written here are exactly the names read back. Long WS-Fed URIs would arrive unreadable.
        var user = CreateUser(UserRole.Customer);
        var subject = CreateSubject();

        var token = new JwtSecurityTokenHandler().ReadJwtToken(subject.GenerateAccessToken(user));

        Assert.Equal(user.Id.ToString(), token.Claims.Single(c => c.Type == Constants.UserIdClaimType).Value);
        Assert.Equal(user.Email.Value, token.Claims.Single(c => c.Type == Constants.EmailClaimType).Value);
        Assert.Equal(nameof(UserRole.Customer), token.Claims.Single(c => c.Type == Constants.RoleClaimType).Value);
    }

    [Fact]
    public void GenerateAccessToken_Honours_Configured_Expiration()
    {
        var subject = CreateSubject(accessTokenExpirationMinutes: 60);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(subject.GenerateAccessToken(CreateUser(UserRole.Customer)));

        var minutes = (token.ValidTo - DateTime.UtcNow).TotalMinutes;
        Assert.InRange(minutes, 58, 60);
    }

    [Fact]
    public void GenerateAccessToken_Throws_When_Expiration_Not_Configured()
    {
        var subject = CreateSubject(accessTokenExpirationMinutes: 0);

        Assert.Throws<InvalidOperationException>(() => subject.GenerateAccessToken(CreateUser(UserRole.Customer)));
    }

    [Fact]
    public void GenerateAccessToken_Sets_Issuer_And_Audience()
    {
        var subject = CreateSubject();

        var token = new JwtSecurityTokenHandler().ReadJwtToken(subject.GenerateAccessToken(CreateUser(UserRole.Admin)));

        Assert.Equal("https://localhost:7220", token.Issuer);
        Assert.Contains("https://localhost:7220", token.Audiences);
    }

    private static Auth.Infrastructure.Services.TokenGenerator CreateSubject(int accessTokenExpirationMinutes = 60)
    {
        return new Auth.Infrastructure.Services.TokenGenerator(Options.Create(new JwtOptions
        {
            Issuer = "https://localhost:7220",
            Audience = "https://localhost:7220",
            SecretKey = SecretKey,
            AccessTokenExpirationMinutes = accessTokenExpirationMinutes,
            RefreshTokenExpirationDays = 7
        }));
    }

    private static User CreateUser(UserRole role) => role switch
    {
        UserRole.Admin => User.RegisterAdmin("Ada", "Admin", Email.Create("admin@example.com"), "+15551234567", "hash"),
        UserRole.Staff => User.RegisterStaff("Sam", "Staff", Email.Create("staff@example.com"), "+15551234567", "hash"),
        _ => User.RegisterCustomer("Cam", "Customer", Email.Create("customer@example.com"), "+15551234567", "hash")
    };
}
