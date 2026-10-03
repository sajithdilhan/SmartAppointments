using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace SmartAppointments.BuildingBlocks.Web.Middlewares;

/// <summary>
/// Gives every request one correlation id: the caller's <c>X-Correlation-ID</c> when it is well
/// formed, a fresh one otherwise. The id is kept in <see cref="HttpContext.Items"/>, written back to
/// the request header (so a proxy forwards the decided value, never the original), echoed on the
/// response and added to the logger scope. Register it ahead of <see cref="ExceptionMiddleware"/> so
/// error responses and the error log carry it too.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string ItemKey = "SmartAppointments.CorrelationId";
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var id = Resolve(context.Request.Headers[Constants.CorrelationIdHeaderName]);

        context.Items[ItemKey] = id;
        context.Request.Headers[Constants.CorrelationIdHeaderName] = id;
        // Assigned when the headers are about to be sent, so a header copied from a proxied response
        // is replaced rather than appended to.
        context.Response.OnStarting(static state =>
        {
            var (ctx, value) = ((HttpContext, string))state;
            ctx.Response.Headers[Constants.CorrelationIdHeaderName] = value;
            return Task.CompletedTask;
        }, (context, id));

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            await next(context);
        }
    }

    internal static string Resolve(StringValues header)
    {
        if (header.Count == 1 && IsValid(header[0]))
        {
            return header[0]!;
        }

        return Guid.NewGuid().ToString("N");
    }

    private static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            var allowed = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}

public static class CorrelationIdExtensions
{
    /// <summary>The correlation id <see cref="CorrelationIdMiddleware"/> decided for this request, if it ran.</summary>
    public static string? GetCorrelationId(this HttpContext context) =>
        context.Items[CorrelationIdMiddleware.ItemKey] as string;
}
