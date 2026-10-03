using Microsoft.AspNetCore.Http;
using SmartAppointments.BuildingBlocks.Web.Middlewares;

namespace SmartAppointments.BuildingBlocks.Web.Http;

/// <summary>
/// Forwards the current request's correlation id on an outgoing <see cref="HttpClient"/> call.
/// Add it ahead of any retry handler so every attempt carries the same id.
/// </summary>
public sealed class CorrelationIdHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var id = accessor.HttpContext?.GetCorrelationId();
        if (id is not null && !request.Headers.Contains(Constants.CorrelationIdHeaderName))
        {
            request.Headers.Add(Constants.CorrelationIdHeaderName, id);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
