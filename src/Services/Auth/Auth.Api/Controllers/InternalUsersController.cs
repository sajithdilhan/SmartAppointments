using Auth.Application.Models;
using Auth.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;
using SmartAppointments.BuildingBlocks.Web.Results;

namespace Auth.Api.Controllers;

/// <summary>
/// Service-to-service calls from Booking, authenticated by API key only. Kept apart from the public
/// controllers so the two surfaces cannot share an attribute by accident; the gateway never routes
/// <c>/internal/**</c>.
/// </summary>
[Route("internal/users")]
[ApiController]
[Authorize(Policy = Constants.InternalServicePolicy)]
public class InternalUsersController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Returns the display data of the given users in one call. Unknown ids are omitted and
    /// duplicates are returned once.
    /// </summary>
    [HttpPost("lookup")]
    [ProducesResponseType<IReadOnlyList<UserLookupItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Lookup(LookupUsersRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new LookupUsersQuery(request.Ids), cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return Ok(result.Value);
    }
}
