using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmartAppointments.Gateway.Health;

/// <summary>
/// Calls one downstream service's <c>/healthz</c>. A non-2xx answer, a timeout or an unreachable
/// service is reported with the registration's <c>FailureStatus</c> (Degraded for the downstreams),
/// because the health service applies that status only to checks that throw. Descriptions never carry
/// an exception message or a host name.
/// </summary>
public sealed class DownstreamHealthCheck(Uri healthUrl, IHttpClientFactory factory) : IHealthCheck
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    public const string ClientName = "health";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var failure = context.Registration.FailureStatus;
        using var timeout = new CancellationTokenSource(Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var client = factory.CreateClient(ClientName);
            using var response = await client.GetAsync(healthUrl, HttpCompletionOption.ResponseHeadersRead, linked.Token);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(failure, $"HTTP {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(failure, "timed out");
        }
        catch (HttpRequestException)
        {
            return new HealthCheckResult(failure, "unreachable");
        }
    }
}
