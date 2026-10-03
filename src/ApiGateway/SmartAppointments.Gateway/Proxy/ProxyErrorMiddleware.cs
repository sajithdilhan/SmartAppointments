using SmartAppointments.BuildingBlocks.Web.Results;
using Yarp.ReverseProxy.Forwarder;

namespace SmartAppointments.Gateway.Proxy;

/// <summary>
/// YARP does not throw when it cannot reach a service: it records the failure in
/// <see cref="IForwarderErrorFeature"/>, sets a status code and writes no body. This step, in the
/// proxy pipeline ahead of the forwarder, turns that into <c>application/problem+json</c>. The
/// details are fixed strings; YARP has already logged the exception.
/// </summary>
public sealed class ProxyErrorMiddleware(RequestDelegate next)
{
    public const string UnavailableMessage = "A downstream service is unavailable.";
    public const string TimeoutMessage = "A downstream service did not respond in time.";

    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        var feature = context.Features.Get<IForwarderErrorFeature>();
        if (feature is null || feature.Error == ForwarderError.None || context.Response.HasStarted)
        {
            return;
        }

        switch (feature.Error)
        {
            case ForwarderError.RequestTimedOut:
                await ProblemDetailsWriter.WriteAsync(context, StatusCodes.Status504GatewayTimeout, TimeoutMessage);
                break;

            case ForwarderError.RequestCanceled:
            case ForwarderError.RequestBodyCanceled:
            case ForwarderError.ResponseBodyCanceled:
                // The client left; there is nobody to write to.
                break;

            default:
                await ProblemDetailsWriter.WriteAsync(context, StatusCodes.Status502BadGateway, UnavailableMessage);
                break;
        }
    }
}
