using Microsoft.Extensions.Configuration;

namespace Gateway.Tests;

public class RoutingConfigurationTests
{
    private static IConfigurationRoot Shipped() => new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .Build();

    private static IConfigurationRoot Development() => new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile("appsettings.Development.json", optional: false)
        .Build();

    private sealed record Route(string Name, string ClusterId, int Order, string? Policy, string Path, string[] Methods, string? RateLimiter);

    private static List<Route> Routes(IConfiguration configuration) => configuration
        .GetSection("ReverseProxy:Routes").GetChildren()
        .Select(r => new Route(
            r.Key,
            r["ClusterId"]!,
            int.Parse(r["Order"] ?? "0"),
            r["AuthorizationPolicy"],
            r["Match:Path"]!,
            r.GetSection("Match:Methods").GetChildren().Select(m => m.Value!).ToArray(),
            r["RateLimiterPolicy"]))
        .ToList();

    private static void AssertRoute(Route route, string path, string cluster, string? method = null)
    {
        Assert.Equal(path, route.Path);
        Assert.Equal(cluster, route.ClusterId);
        Assert.Equal(method is null ? [] : new[] { method }, route.Methods);
    }

    [Fact]
    public void The_Shipped_Configuration_Has_The_Eleven_Routes()
    {
        var routes = Routes(Shipped()).ToDictionary(r => r.Name);

        Assert.Equal(
            ["appointments", "appointments-create", "auth", "auth-login", "auth-logout", "auth-refresh", "auth-register", "branches", "services", "slots", "slots-search"],
            routes.Keys.Order().ToArray());

        AssertRoute(routes["auth-login"], "/api/auth/login", "auth", "POST");
        AssertRoute(routes["auth-refresh"], "/api/auth/refresh", "auth", "POST");
        AssertRoute(routes["auth-logout"], "/api/auth/logout", "auth", "POST");
        AssertRoute(routes["auth-register"], "/api/auth/register", "auth", "POST");
        AssertRoute(routes["auth"], "/api/auth/{**catch-all}", "auth");
        AssertRoute(routes["branches"], "/api/branches/{**catch-all}", "availability");
        AssertRoute(routes["services"], "/api/services/{**catch-all}", "availability");
        AssertRoute(routes["slots-search"], "/api/slots/available", "availability", "GET");
        AssertRoute(routes["slots"], "/api/slots/{**catch-all}", "availability");
        AssertRoute(routes["appointments-create"], "/api/appointments", "booking", "POST");
        AssertRoute(routes["appointments"], "/api/appointments/{**catch-all}", "booking");
    }

    [Fact]
    public void Exact_Routes_Are_Ordered_Ahead_Of_The_Prefix_Routes()
    {
        foreach (var route in Routes(Shipped()))
        {
            var exact = route.Methods.Length > 0;
            Assert.Equal(exact ? 0 : 10, route.Order);
        }
    }

    public static TheoryData<string> ConfigurationNames => new() { "appsettings.json", "development" };

    [Theory]
    [MemberData(nameof(ConfigurationNames))]
    public void Nothing_Is_Routed_To_Internal_Health_Or_As_A_Root_Catch_All(string name)
    {
        IConfiguration configuration = name == "development" ? Development() : Shipped();

        foreach (var route in Routes(configuration))
        {
            Assert.False(route.Path.StartsWith("/internal", StringComparison.OrdinalIgnoreCase), $"{name}: {route.Name}");
            Assert.False(route.Path.StartsWith("/healthz", StringComparison.OrdinalIgnoreCase), $"{name}: {route.Name}");
            Assert.NotEqual("/{**catch-all}", route.Path);
        }
    }

    [Fact]
    public void Every_Route_Names_A_Policy_And_Only_The_Auth_Entry_Routes_Are_Anonymous()
    {
        foreach (var route in Routes(Development()))
        {
            Assert.False(string.IsNullOrWhiteSpace(route.Policy), route.Name);
        }

        var anonymous = Routes(Shipped()).Where(r => r.Policy == "anonymous").Select(r => r.Name).Order().ToArray();
        Assert.Equal(["auth-login", "auth-logout", "auth-refresh", "auth-register"], anonymous);
        Assert.All(Routes(Shipped()).Where(r => r.Policy != "anonymous"), r => Assert.Equal("default", r.Policy));
    }

    [Fact]
    public void Every_Route_Forwards_The_Original_Host_So_Absolute_Urls_Name_The_Gateway()
    {
        foreach (var configuration in new IConfiguration[] { Shipped(), Development() })
        {
            foreach (var route in configuration.GetSection("ReverseProxy:Routes").GetChildren())
            {
                Assert.Contains(
                    route.GetSection("Transforms").GetChildren(),
                    t => t["RequestHeaderOriginalHost"] == "true");
            }
        }
    }

    [Theory]
    [InlineData("auth-refresh", "/api/auth/refresh")]
    [InlineData("auth-logout", "/api/auth/logout")]
    public void Refresh_And_Logout_Are_Anonymous_Order_Zero_Post_Routes_To_Auth_Like_Login(string name, string path)
    {
        var routes = Routes(Shipped()).ToDictionary(r => r.Name);
        var route = routes[name];

        Assert.Equal(path, route.Path);
        Assert.Equal("auth", route.ClusterId);
        Assert.Equal(0, route.Order);
        Assert.Equal("anonymous", route.Policy);
        Assert.Equal(["POST"], route.Methods);
        Assert.Equal(routes["auth-login"].Order, route.Order);
        Assert.Equal(routes["auth-login"].Policy, route.Policy);
    }

    [Fact]
    public void Other_Verbs_On_Refresh_And_Logout_Fall_Through_To_The_Authenticated_Auth_Catch_All()
    {
        var routes = Routes(Shipped()).ToDictionary(r => r.Name);

        Assert.Equal(["POST"], routes["auth-refresh"].Methods);
        Assert.Equal(["POST"], routes["auth-logout"].Methods);
        Assert.Empty(routes["auth"].Methods);
        Assert.Equal(10, routes["auth"].Order);
        Assert.Equal("default", routes["auth"].Policy);
    }

    [Fact]
    public void Only_Login_Refresh_Slot_Search_And_Appointment_Create_Name_A_Rate_Limiter_Policy()
    {
        var limited = Routes(Shipped()).Where(r => r.RateLimiter is not null).ToDictionary(r => r.Name, r => r.RateLimiter);

        Assert.Equal(4, limited.Count);
        Assert.Equal("login", limited["auth-login"]);
        Assert.Equal("refresh", limited["auth-refresh"]);
        Assert.False(limited.ContainsKey("auth-logout"));
        Assert.Equal("appointment-create", limited["appointments-create"]);
        Assert.Equal("slot-search", limited["slots-search"]);
    }

    [Fact]
    public void The_Shipped_Configuration_Has_Three_Clusters_With_Blank_Addresses_And_A_30_Second_Timeout()
    {
        var clusters = Shipped().GetSection("ReverseProxy:Clusters").GetChildren().ToList();

        Assert.Equal(["auth", "availability", "booking"], clusters.Select(c => c.Key).Order().ToArray());
        foreach (var cluster in clusters)
        {
            var destinations = cluster.GetSection("Destinations").GetChildren().ToList();
            Assert.Single(destinations);
            Assert.Equal(string.Empty, destinations[0]["Address"]);
            Assert.Equal("00:00:30", cluster["HttpRequest:ActivityTimeout"]);
        }
    }

    [Fact]
    public void Development_Fills_The_Addresses_Of_The_Three_Services()
    {
        var configuration = Development();

        Assert.Equal("http://localhost:5008", configuration["ReverseProxy:Clusters:auth:Destinations:primary:Address"]);
        Assert.Equal("http://localhost:5202", configuration["ReverseProxy:Clusters:availability:Destinations:primary:Address"]);
        Assert.Equal("http://localhost:5310", configuration["ReverseProxy:Clusters:booking:Destinations:primary:Address"]);
    }

    [Fact]
    public void Every_Route_Points_At_A_Configured_Cluster()
    {
        var configuration = Development();
        var clusters = configuration.GetSection("ReverseProxy:Clusters").GetChildren().Select(c => c.Key).ToHashSet();

        Assert.All(Routes(configuration), r => Assert.Contains(r.ClusterId, clusters));
    }

    [Fact]
    public void The_Jwt_Section_Matches_Auth_And_Ships_Without_A_Secret()
    {
        var shipped = Shipped();

        Assert.Equal("https://localhost:7220", shipped["Jwt:Issuer"]);
        Assert.Equal("https://localhost:7220", shipped["Jwt:Audience"]);
        Assert.Equal(string.Empty, shipped["Jwt:SecretKey"]);
        Assert.False(string.IsNullOrEmpty(Development()["Jwt:SecretKey"]));
    }

    [Fact]
    public void Console_Log_Scopes_Are_Enabled()
    {
        Assert.Equal("True", Shipped()["Logging:Console:IncludeScopes"], ignoreCase: true);
    }
}
