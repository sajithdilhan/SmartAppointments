using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;
using SmartAppointments.BuildingBlocks.Web.Results;

namespace SmartAppointments.Gateway.RateLimiting;

/// <summary>
/// The gateway's rate limits: in-memory sliding windows (1 minute, 6 segments, no queue). The counters
/// live in this process, so a second gateway instance would allow the full limit again and a restart
/// forgets every count (accepted single-instance limitation, Requirement 3.9).
/// </summary>
public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string Refresh = "refresh";
    public const string AppointmentCreate = "appointment-create";
    public const string SlotSearch = "slot-search";

    public const int LoginPermits = 5;
    public const int RefreshPermits = 10;
    public const int AppointmentCreatePermits = 10;
    public const int SlotSearchPermits = 30;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    public const int Segments = 6;

    /// <summary>Partition by the connection's address; forwarded headers are never read, so they cannot pick a partition.</summary>
    public static RateLimitPartition<string> ByClientIp(HttpContext context)
        => ByClientIp(context, LoginPermits);

    /// <summary>
    /// The same partitioning with its own permit count. Each policy name gets its own limiter, so the
    /// same address key in two policies is two separate counters.
    /// </summary>
    public static RateLimitPartition<string> ByClientIp(HttpContext context, int permits)
        => RateLimitPartition.GetSlidingWindowLimiter(IpKey(context), _ => Options(permits));

    /// <summary>Partition by the validated <c>sub</c> claim, falling back to the client address without one.</summary>
    public static RateLimitPartition<string> ByUser(HttpContext context, string policy, int permits)
    {
        var sub = context.User.FindFirst(Constants.UserIdClaimType)?.Value;
        var key = string.IsNullOrEmpty(sub) ? IpKey(context) : $"{policy}:{sub}";
        return RateLimitPartition.GetSlidingWindowLimiter(key, _ => Options(permits));
    }

    private static string IpKey(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return "ip:unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return $"ip:{address}";
    }

    private static SlidingWindowRateLimiterOptions Options(int permits) => new()
    {
        PermitLimit = permits,
        Window = Window,
        SegmentsPerWindow = Segments,
        QueueLimit = 0,
        AutoReplenishment = true
    };

    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services)
    {
        return services.AddRateLimiter(options =>
        {
            options.AddPolicy(Login, context => ByClientIp(context, LoginPermits));
            options.AddPolicy(Refresh, context => ByClientIp(context, RefreshPermits));
            options.AddPolicy(AppointmentCreate, context => ByUser(context, AppointmentCreate, AppointmentCreatePermits));
            options.AddPolicy(SlotSearch, context => ByUser(context, SlotSearch, SlotSearchPermits));
            options.OnRejected = RejectAsync;
        });
    }

    /// <summary>Writes the 429 problem+json with a <c>Retry-After</c> in whole seconds (at least 1).</summary>
    public static async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;

        var seconds = (int)Window.TotalSeconds;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
        }

        seconds = Math.Max(seconds, 1);
        http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

        // Policy name only: no user id, no address, no token.
        var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        http.RequestServices.GetService<ILoggerFactory>()?
            .CreateLogger(typeof(RateLimitPolicies).FullName!)
            .LogWarning("Rate limit exceeded for policy {Policy}", policy ?? "unknown");

        await ProblemDetailsWriter.WriteAsync(http, new ApiProblemDetails(StatusCodes.Status429TooManyRequests, "Too many requests. Try again later."));
    }
}
