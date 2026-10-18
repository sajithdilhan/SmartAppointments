using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auth.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Infrastructure.Clients;

/// <summary>
/// Calls Availability's internal API. The resilience handler on the underlying <see cref="HttpClient"/>
/// does the retrying; this class only maps what finally comes back (or fails) to a <see cref="Result{T}"/>.
/// </summary>
public sealed class AvailabilityClient(HttpClient httpClient, ILogger<AvailabilityClient> logger) : IAvailabilityClient
{
    public const string UnavailableMessage = "The availability service is unavailable. Try again later.";

    private static readonly Error Unavailable = new(503, UnavailableMessage);

    public async Task<Result<BranchInfo?>> GetBranchAsync(Guid branchId, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            () => httpClient.GetAsync($"internal/branches/{branchId}", cancellationToken), cancellationToken);
        if (response is null)
        {
            return Result<BranchInfo?>.Failure(Unavailable);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    try
                    {
                        var branch = await response.Content.ReadFromJsonAsync<BranchInfo>(cancellationToken);
                        if (branch is not null)
                        {
                            return Result<BranchInfo?>.Success(branch);
                        }
                    }
                    catch (JsonException ex)
                    {
                        logger.LogError(ex, "Availability returned an unreadable branch body");
                    }

                    return Result<BranchInfo?>.Failure(Unavailable);
                case HttpStatusCode.NotFound:
                    return Result<BranchInfo?>.Success(null);
                default:
                    LogRejection(response);
                    return Result<BranchInfo?>.Failure(Unavailable);
            }
        }
    }

    // Returns null for every kind of outage so callers only ever see a 503.
    private async Task<HttpResponseMessage?> SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        try
        {
            return await send();
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Availability could not be reached");
        }
        catch (TimeoutRejectedException ex)
        {
            logger.LogWarning(ex, "The call to Availability timed out");
        }
        catch (BrokenCircuitException ex)
        {
            logger.LogWarning(ex, "The circuit to Availability is open");
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Not the caller giving up: a timeout inside the HTTP stack.
            logger.LogWarning(ex, "The call to Availability timed out");
        }

        return null;
    }

    // 401/403 mean our API key is wrong (a configuration fault); everything else, including an
    // exhausted retry, is an outage. Both reach the caller as 503.
    private void LogRejection(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogError("Availability rejected the internal API key with {StatusCode}; check InternalApi:Key", (int)response.StatusCode);
        }
        else
        {
            logger.LogWarning("Availability answered {StatusCode}", (int)response.StatusCode);
        }
    }
}
