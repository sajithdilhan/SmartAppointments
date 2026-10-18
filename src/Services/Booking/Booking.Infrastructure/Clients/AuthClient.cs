using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Booking.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Infrastructure.Clients;

/// <summary>
/// Calls Auth's internal user lookup. The resilience handler on the underlying <see cref="HttpClient"/>
/// does the retrying; this class only maps what finally comes back (or fails) to a <see cref="Result{T}"/>.
/// Every outage, 401/403 included, is <c>Failure(503)</c>.
/// </summary>
public sealed class AuthClient(HttpClient httpClient, ILogger<AuthClient> logger) : IAuthClient
{
    public const string UnavailableMessage = "The auth service is unavailable. Try again later.";

    private static readonly Error Unavailable = new(503, UnavailableMessage);

    // Mirrors Auth's UserLookupItem.
    private sealed record LookupItem(Guid Id, string FirstName, string LastName, string PhoneNumber);

    public async Task<Result<IReadOnlyDictionary<Guid, CustomerInfo>>> LookupAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            // A POST because the ids travel in the body, but it only reads, so the standard retry
            // strategy (which retries every method) is safe here.
            response = await httpClient.PostAsJsonAsync("internal/users/lookup", new { ids }, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Auth could not be reached");
            return Result<IReadOnlyDictionary<Guid, CustomerInfo>>.Failure(Unavailable);
        }
        catch (TimeoutRejectedException ex)
        {
            logger.LogWarning(ex, "The call to Auth timed out");
            return Result<IReadOnlyDictionary<Guid, CustomerInfo>>.Failure(Unavailable);
        }
        catch (BrokenCircuitException ex)
        {
            logger.LogWarning(ex, "The circuit to Auth is open");
            return Result<IReadOnlyDictionary<Guid, CustomerInfo>>.Failure(Unavailable);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Not the caller giving up: a timeout inside the HTTP stack.
            logger.LogWarning(ex, "The call to Auth timed out");
            return Result<IReadOnlyDictionary<Guid, CustomerInfo>>.Failure(Unavailable);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                try
                {
                    var items = await response.Content.ReadFromJsonAsync<List<LookupItem>>(cancellationToken);
                    if (items is not null)
                    {
                        IReadOnlyDictionary<Guid, CustomerInfo> map = items
                            .GroupBy(i => i.Id)
                            .ToDictionary(g => g.Key, g => new CustomerInfo(
                                g.First().FirstName, g.First().LastName, g.First().PhoneNumber));
                        return Result<IReadOnlyDictionary<Guid, CustomerInfo>>.Success(map);
                    }
                }
                catch (JsonException ex)
                {
                    logger.LogError(ex, "Auth returned an unreadable lookup body");
                }

                return Result<IReadOnlyDictionary<Guid, CustomerInfo>>.Failure(Unavailable);
            }

            // 401/403 mean our API key is wrong (a configuration fault); everything else, including
            // an exhausted retry, is an outage.
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                logger.LogError("Auth rejected the internal API key with {StatusCode}; check InternalApi:Key", (int)response.StatusCode);
            }
            else
            {
                logger.LogWarning("Auth answered {StatusCode}", (int)response.StatusCode);
            }

            return Result<IReadOnlyDictionary<Guid, CustomerInfo>>.Failure(Unavailable);
        }
    }
}
