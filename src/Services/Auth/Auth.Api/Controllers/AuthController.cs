using Auth.Application.Commands;
using Auth.Application.Models;
using Auth.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController(ISender sender) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(UserLoginRequest loginRequest)
    {
        var command = new LoginUserCommand(loginRequest.Email, loginRequest.Password);

        var result = await sender.Send(command);

        if (!result.IsSuccess)
        {
            return ToErrorResult(result.Error!);
        }

        return Ok(result.Value);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterCustomerRequest customerRequest, CancellationToken cancellationToken)
    {
        var command = new RegisterCustomerCommand(
             customerRequest.FirstName,
             customerRequest.LastName,
             customerRequest.Email,
             customerRequest.PhoneNumber,
             customerRequest.Password);

        var result = await sender.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return ToErrorResult(result.Error!);
        }

        return CreatedAtAction(
            nameof(GetProfile),
            new { email = result.Value!.Email },
            result.Value);
    }

    /// <summary>
    /// Looks up a profile by email. A Customer caller always gets their own profile back
    /// regardless of what they ask for; Staff and Admin get the address they requested.
    /// </summary>
    [HttpGet("profile")]
    [Authorize(Policy = Constants.AllowedOriginsPolicy)]
    public async Task<IActionResult> GetProfile(string email, CancellationToken cancellationToken)
    {
        return await SendProfileQuery(email, cancellationToken);
    }

    /// <summary>
    /// Returns the caller's own profile, resolved from the token, with no email parameter to
    /// supply or ignore. Callers who need to look up somebody else use <see cref="GetProfile"/>.
    /// </summary>
    [HttpGet("me")]
    [Authorize(Policy = Constants.AllowedOriginsPolicy)]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
    {
        var currentUserEmail = User.FindFirst(Constants.EmailClaimType)?.Value;
        return await SendProfileQuery(currentUserEmail ?? string.Empty, cancellationToken);
    }

    private async Task<IActionResult> SendProfileQuery(string email, CancellationToken cancellationToken)
    {
        var currentUserEmail = User.FindFirst(Constants.EmailClaimType)?.Value;
        var currentUserRole = User.FindFirst(Constants.RoleClaimType)?.Value;
        var query = new GetCustomerQuery(email, currentUserEmail, currentUserRole);
        var result = await sender.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return ToErrorResult(result.Error!);
        }

        return Ok(result.Value);
    }

    // Every failure path goes through here so the status a handler chose is the status the caller
    // sees. Branching on IsSuccess alone used to collapse 403 into 404 and 400 into 401.
    private ObjectResult ToErrorResult(Error error) => error.Status switch
    {
        StatusCodes.Status400BadRequest => BadRequest(error),
        StatusCodes.Status401Unauthorized => Unauthorized(error),
        StatusCodes.Status403Forbidden => StatusCode(StatusCodes.Status403Forbidden, error),
        StatusCodes.Status404NotFound => NotFound(error),
        StatusCodes.Status409Conflict => Conflict(error),
        _ => StatusCode(StatusCodes.Status500InternalServerError, error)
    };
}
