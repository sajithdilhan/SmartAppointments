using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Net.Http.Headers;

namespace SmartAppointments.Gateway.Cors;

/// <summary>
/// Defence in depth: the services send no Access-Control-* header, but YARP copies whatever a service
/// does send after the CORS middleware has written its own, which could duplicate a name or leak one the
/// policy never sends (Allow-Credentials). This drops those names and lets the policy apply its own again.
/// </summary>
public static class DownstreamCorsHeaderTransform
{
    private const string Prefix = "Access-Control-";

    public static void Apply(HttpContext context, HttpResponseMessage? proxyResponse, ICorsService cors, CorsPolicy policy)
    {
        if (proxyResponse is null)
        {
            return;
        }

        var sent = proxyResponse.Headers.Select(h => h.Key)
            .Concat(proxyResponse.Content.Headers.Select(h => h.Key))
            .Where(name => name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sent.Count == 0)
        {
            return;
        }

        var headers = context.Response.Headers;
        foreach (var name in sent)
        {
            headers.Remove(name);
        }

        if (!context.Request.Headers.ContainsKey(HeaderNames.Origin))
        {
            return;
        }

        var result = cors.EvaluatePolicy(context, policy);
        if (!result.IsOriginAllowed)
        {
            return;
        }

        // The middleware already added `Vary: Origin`; ApplyResult would add it a second time.
        var vary = headers.Vary;
        cors.ApplyResult(result, context.Response);
        headers.Vary = vary;
    }
}
