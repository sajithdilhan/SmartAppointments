namespace SmartAppointments.Gateway.Proxy;

/// <summary>
/// Answers <c>404</c> for a request that matched no endpoint, ahead of authentication. ASP.NET Core
/// applies the fallback authorization policy to such a request too, so without this step an unknown
/// path (<c>/internal/...</c>, a service's <c>/healthz</c>) would get <c>401</c> instead of <c>404</c>
/// and demand a token to say "not found" (Requirement 1.3, 1.9).
/// </summary>
public sealed class UnmatchedRequestMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint() is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return next(context);
    }
}
