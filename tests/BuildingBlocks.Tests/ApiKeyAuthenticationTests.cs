using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Web.Authentication;
using System.Security.Claims;

namespace BuildingBlocks.Tests;

public class ApiKeyAuthenticationTests
{
    private const string ValidKey = "internal-key-0123456789abcdef-0123456789";
    private const string JwtKey = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task A_Request_Without_The_Header_Has_No_Result()
    {
        var result = await Authenticate(ServiceProvider(), headerValue: null);

        Assert.False(result.Succeeded);
        Assert.True(result.None);
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("")]
    [InlineData("internal-key-0123456789abcdef-0123456789-and-more")]
    [InlineData("internal-key-0123456789abcdef-012345678")]
    public async Task A_Wrong_Or_Different_Length_Key_Fails_Without_Echoing_Either_Key(string presented)
    {
        var result = await Authenticate(ServiceProvider(), presented);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Equal("Invalid API key.", result.Failure.Message);
        Assert.DoesNotContain(ValidKey, result.Failure.Message);
    }

    [Fact]
    public async Task The_Configured_Key_Authenticates_As_The_Internal_Service_With_No_Roles()
    {
        var result = await Authenticate(ServiceProvider(), ValidKey);

        Assert.True(result.Succeeded);
        var identity = Assert.Single(result.Principal!.Identities);
        Assert.Equal(Constants.ApiKeyAuthenticationScheme, identity.AuthenticationType);
        Assert.Equal(ApiKeyAuthenticationHandler.ServiceName, identity.Name);
        Assert.DoesNotContain(result.Principal.Claims, c => c.Type is ClaimTypes.Role or Constants.RoleClaimType);
    }

    [Theory]
    [InlineData(null, "InternalApi:Key")]
    [InlineData("", "InternalApi:Key")]
    [InlineData("   ", "InternalApi:Key")]
    [InlineData("0123456789abcdef0123456789abcde", "at least 32 bytes")]
    public void A_Missing_Blank_Or_Short_Key_Stops_Startup(string? key, string expectedMessage)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddApiKeyAuthentication(Configuration(key)));

        Assert.Contains(expectedMessage, ex.Message);
    }

    [Fact]
    public async Task The_Default_Schemes_Stay_Jwt_Bearer()
    {
        var provider = ServiceProvider();
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, (await schemes.GetDefaultAuthenticateSchemeAsync())!.Name);
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, (await schemes.GetDefaultChallengeSchemeAsync())!.Name);
        Assert.NotNull(await schemes.GetSchemeAsync(Constants.ApiKeyAuthenticationScheme));
    }

    [Fact]
    public async Task The_Internal_Policy_Accepts_Only_The_Api_Key_Scheme()
    {
        var policy = await Policy(ServiceProvider(), Constants.InternalServicePolicy);

        Assert.Equal([Constants.ApiKeyAuthenticationScheme], policy.AuthenticationSchemes);
        Assert.Single(policy.Requirements.OfType<DenyAnonymousAuthorizationRequirement>());
    }

    [Fact]
    public async Task The_Api_Key_Satisfies_The_Internal_Policy()
    {
        var provider = ServiceProvider();
        var context = Context(provider, ValidKey);

        var result = await Evaluate(provider, Constants.InternalServicePolicy, context);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task A_Bearer_Token_Does_Not_Satisfy_The_Internal_Policy()
    {
        var provider = ServiceProvider();
        var context = Context(provider, headerValue: null);
        context.Request.Headers.Authorization = "Bearer some.jwt.token";

        var result = await Evaluate(provider, Constants.InternalServicePolicy, context);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(Constants.AdminPolicy)]
    [InlineData(Constants.StaffPolicy)]
    [InlineData(Constants.CustomerPolicy)]
    [InlineData(Constants.AdminOrStaffPolicy)]
    [InlineData(Constants.AllowedOriginsPolicy)]
    public async Task The_Api_Key_Alone_Is_Unauthenticated_For_Every_Role_Policy(string policyName)
    {
        var provider = ServiceProvider();
        var context = Context(provider, ValidKey);

        var result = await Evaluate(provider, policyName, context);

        Assert.False(result.Succeeded);
    }

    private static IConfiguration Configuration(string? key) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InternalApi:Key"] = key,
                ["Jwt:SecretKey"] = JwtKey,
                ["Jwt:Issuer"] = "issuer",
                ["Jwt:Audience"] = "audience"
            })
            .Build();

    // Wired the way the services do it: JWT first, then the API key, then the role policies.
    private static ServiceProvider ServiceProvider()
    {
        var configuration = Configuration(ValidKey);
        var services = new ServiceCollection().AddLogging();
        services.AddJwtAuthentication(configuration);
        services.AddApiKeyAuthentication(configuration);
        services.AddAuthorizationWithRoles();
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext Context(IServiceProvider provider, string? headerValue)
    {
        var context = new DefaultHttpContext { RequestServices = provider };
        if (headerValue is not null)
        {
            context.Request.Headers[Constants.ApiKeyHeaderName] = headerValue;
        }

        return context;
    }

    private static Task<AuthenticateResult> Authenticate(IServiceProvider provider, string? headerValue) =>
        Context(provider, headerValue).AuthenticateAsync(Constants.ApiKeyAuthenticationScheme);

    private static async Task<AuthorizationPolicy> Policy(IServiceProvider provider, string name) =>
        (await provider.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(name))!;

    // Authentication and authorization as the pipeline runs them for a policy.
    private static async Task<PolicyAuthorizationResult> Evaluate(IServiceProvider provider, string policyName, HttpContext context)
    {
        var policy = await Policy(provider, policyName);
        var evaluator = provider.GetRequiredService<IPolicyEvaluator>();
        var authentication = await evaluator.AuthenticateAsync(policy, context);
        return await evaluator.AuthorizeAsync(policy, authentication, context, resource: null);
    }
}
