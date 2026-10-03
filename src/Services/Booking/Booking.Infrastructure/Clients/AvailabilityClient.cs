using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Booking.Application.Abstractions;
using Booking.Application.Models;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Infrastructure.Clients;

/// <summary>
/// Calls Availability's internal API. The resilience handler on the underlying <see cref="HttpClient"/>
/// does the retrying; this class only maps what finally comes back (or fails) to a <see cref="Result{T}"/>.
/// </summary>
public sealed class AvailabilityClient(HttpClient httpClient, ILogger<AvailabilityClient> logger) : IAvailabilityClient
{
    public const string UnavailableMessage = "The availability service is unavailable. Try again later.";

    private static readonly Error Unavailable = new(503, UnavailableMessage);
    private static readonly Error SlotNotFound = new(404, "Slot not found.");

    public async Task<Result<SlotInfo>> GetSlotAsync(Guid slotId, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            () => httpClient.GetAsync($"internal/slots/{slotId}", cancellationToken), cancellationToken);
        if (response is null)
        {
            return Result<SlotInfo>.Failure(Unavailable);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    try
                    {
                        var slot = await response.Content.ReadFromJsonAsync<SlotInfo>(cancellationToken);
                        if (slot is not null)
                        {
                            return Result<SlotInfo>.Success(slot);
                        }
                    }
                    catch (JsonException ex)
                    {
                        logger.LogError(ex, "Availability returned an unreadable slot body");
                    }

                    return Result<SlotInfo>.Failure(Unavailable);
                case HttpStatusCode.NotFound:
                    return Result<SlotInfo>.Failure(SlotNotFound);
                default:
                    return Result<SlotInfo>.Failure(await MapFailureAsync(response, cancellationToken));
            }
        }
    }

    public Task<Result<bool>> ReserveAsync(Guid slotId, Guid appointmentId, CancellationToken cancellationToken) =>
        PostAsync($"internal/slots/{slotId}/reserve", appointmentId, cancellationToken);

    public Task<Result<bool>> ReleaseAsync(Guid slotId, Guid appointmentId, CancellationToken cancellationToken) =>
        PostAsync($"internal/slots/{slotId}/release", appointmentId, cancellationToken);

    private async Task<Result<bool>> PostAsync(string path, Guid appointmentId, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            () => httpClient.PostAsJsonAsync(path, new { appointmentId }, cancellationToken), cancellationToken);
        if (response is null)
        {
            return Result<bool>.Failure(Unavailable);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return Result<bool>.Success(true);
            }

            return response.StatusCode == HttpStatusCode.NotFound
                ? Result<bool>.Failure(SlotNotFound)
                : Result<bool>.Failure(await MapFailureAsync(response, cancellationToken));
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

    // 409 carries Availability's reason in the shared Error body; 401/403 mean our API key is wrong
    // (a configuration fault, not the customer's); everything else, including an exhausted retry, is an outage.
    private async Task<Error> MapFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            try
            {
                var error = await response.Content.ReadFromJsonAsync<Error>(cancellationToken);
                if (error is not null && !string.IsNullOrWhiteSpace(error.Details))
                {
                    return new Error(409, error.Details);
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Availability returned a 409 with an unreadable body");
            }

            return new Error(409, "The slot cannot be reserved.");
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogError("Availability rejected the internal API key with {StatusCode}; check InternalApi:Key", (int)response.StatusCode);
        }
        else
        {
            logger.LogWarning("Availability answered {StatusCode}", (int)response.StatusCode);
        }

        return Unavailable;
    }
}
