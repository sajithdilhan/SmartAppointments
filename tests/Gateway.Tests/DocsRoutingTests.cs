using Microsoft.Extensions.Configuration;

namespace Gateway.Tests;

public class DocsRoutingTests
{
    private static readonly string[] DocsRoutes = ["docs-auth", "docs-availability", "docs-booking"];

    private static IConfigurationRoot Load(params string[] files)
    {
        var builder = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory);
        foreach (var file in files)
        {
            builder.AddJsonFile(file, optional: false);
        }
        return builder.Build();
    }

    [Fact]
    public void The_Shipped_Configuration_Routes_No_Openapi_Or_Scalar_Path()
    {
        var routes = Load("appsettings.json").GetSection("ReverseProxy:Routes").GetChildren();

        Assert.All(routes, r =>
        {
            var path = r["Match:Path"]!;
            Assert.DoesNotContain("openapi", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("scalar", path, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Development_Adds_Exactly_The_Three_Docs_Routes_To_The_Eleven()
    {
        var shipped = Load("appsettings.json").GetSection("ReverseProxy:Routes").GetChildren().Select(r => r.Key).ToHashSet();
        var development = Load("appsettings.json", "appsettings.Development.json").GetSection("ReverseProxy:Routes").GetChildren().Select(r => r.Key).ToHashSet();

        Assert.Equal(11, shipped.Count);
        Assert.Equal(14, development.Count);
        Assert.Equal(DocsRoutes, development.Except(shipped).Order().ToArray());
    }

    [Theory]
    [InlineData("docs-auth", "auth", "/openapi/auth/v1.json")]
    [InlineData("docs-availability", "availability", "/openapi/availability/v1.json")]
    [InlineData("docs-booking", "booking", "/openapi/booking/v1.json")]
    public void A_Docs_Route_Is_An_Anonymous_Get_That_Rewrites_The_Path_And_Keeps_The_Original_Host(string name, string cluster, string path)
    {
        var route = Load("appsettings.json", "appsettings.Development.json").GetSection($"ReverseProxy:Routes:{name}");

        Assert.Equal(cluster, route["ClusterId"]);
        Assert.Equal("anonymous", route["AuthorizationPolicy"]);
        Assert.Equal(path, route["Match:Path"]);
        Assert.Equal(["GET"], route.GetSection("Match:Methods").GetChildren().Select(m => m.Value!).ToArray());

        var transforms = route.GetSection("Transforms").GetChildren().ToList();
        Assert.Contains(transforms, t => t["PathSet"] == "/openapi/v1.json");
        Assert.Contains(transforms, t => t["RequestHeaderOriginalHost"] == "true");
        Assert.Null(route["RateLimiterPolicy"]);
    }
}
