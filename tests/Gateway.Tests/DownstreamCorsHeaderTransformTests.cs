using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SmartAppointments.Gateway.Cors;

namespace Gateway.Tests;

public class DownstreamCorsHeaderTransformTests
{
    private const string Allowed = "http://localhost:4200";

    private static readonly ServiceProvider Provider = Build();

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGatewayCors([Allowed]);
        return services.BuildServiceProvider();
    }

    private static HttpContext Context(string? origin)
    {
        var context = new DefaultHttpContext();
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        return context;
    }

    // What the response looks like when YARP has copied a service's headers over the middleware's.
    private static void CopyFromService(HttpContext context, HttpResponseMessage proxy)
    {
        foreach (var header in proxy.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
    }

    private static void Run(HttpContext context, HttpResponseMessage proxy)
    {
        var cors = Provider.GetRequiredService<ICorsService>();
        var policy = Provider.GetRequiredService<CorsPolicy>();
        DownstreamCorsHeaderTransform.Apply(context, proxy, cors, policy);
    }

    private static HttpResponseMessage ServiceSendingCorsHeaders()
    {
        var proxy = new HttpResponseMessage();
        proxy.Headers.TryAddWithoutValidation("Access-Control-Allow-Origin", "*");
        proxy.Headers.TryAddWithoutValidation("Access-Control-Allow-Credentials", "true");
        return proxy;
    }

    [Fact]
    public void For_An_Allowed_Origin_Only_The_Policys_Headers_Remain_Each_Once()
    {
        var context = Context(Allowed);
        var cors = Provider.GetRequiredService<ICorsService>();
        var policy = Provider.GetRequiredService<CorsPolicy>();
        cors.ApplyResult(cors.EvaluatePolicy(context, policy), context.Response);
        var proxy = ServiceSendingCorsHeaders();
        CopyFromService(context, proxy);

        DownstreamCorsHeaderTransform.Apply(context, proxy, cors, policy);

        var headers = context.Response.Headers;
        Assert.Equal(Allowed, headers.AccessControlAllowOrigin.ToString());
        Assert.Single(headers.AccessControlAllowOrigin);
        Assert.False(headers.ContainsKey("Access-Control-Allow-Credentials"));
        Assert.Equal("X-Correlation-ID,Retry-After,Location", headers.AccessControlExposeHeaders.ToString());
        Assert.Single(headers.AccessControlExposeHeaders);
        Assert.Equal("Origin", headers.Vary.ToString());
        Assert.Single(headers.Vary);
    }

    [Fact]
    public void For_Another_Origin_No_Access_Control_Header_Remains()
    {
        var context = Context("https://evil.example");
        var proxy = ServiceSendingCorsHeaders();
        CopyFromService(context, proxy);

        Run(context, proxy);

        Assert.DoesNotContain(context.Response.Headers, h => h.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void With_No_Origin_No_Access_Control_Header_Remains()
    {
        var context = Context(null);
        var proxy = ServiceSendingCorsHeaders();
        CopyFromService(context, proxy);

        Run(context, proxy);

        Assert.DoesNotContain(context.Response.Headers, h => h.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_Response_Without_Access_Control_Headers_Is_Left_Unchanged()
    {
        var context = Context(Allowed);
        context.Response.Headers["Access-Control-Allow-Origin"] = Allowed;
        context.Response.Headers["Vary"] = "Origin";
        context.Response.Headers["X-Other"] = "1";
        var proxy = new HttpResponseMessage();
        proxy.Headers.TryAddWithoutValidation("X-Other", "1");

        Run(context, proxy);

        Assert.Equal(3, context.Response.Headers.Count);
        Assert.Equal(Allowed, context.Response.Headers.AccessControlAllowOrigin.ToString());
        Assert.Equal("Origin", context.Response.Headers.Vary.ToString());
    }

    [Fact]
    public void A_Missing_Proxy_Response_Changes_Nothing()
    {
        var context = Context(Allowed);

        Run(context, null!);

        Assert.Empty(context.Response.Headers);
    }
}
