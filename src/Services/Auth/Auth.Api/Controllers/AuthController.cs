using Auth.Application.Commands;
using Auth.Application.Models;
using Auth.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;
using SmartAppointments.BuildingBlocks.Web.Results;

namespace Auth.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController(ISender sender) : ControllerBase
{
    [HttpPost("login")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(UserLoginRequest loginRequest)
    {
        var command = new LoginUserCommand(loginRequest.Email, loginRequest.Password);

        var result = await sender.Send(command);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Exchanges a refresh token for a new access token and a new refresh token. The presented token
    /// is spent; presenting it again revokes the whole session. Anonymous: the refresh token is the credential.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(RefreshTokenRequest refreshRequest, CancellationToken cancellationToken)
    {
        var command = new RefreshTokenCommand(refreshRequest.RefreshToken);

        var result = await sender.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Ends the session the refresh token belongs to. Idempotent: an unknown or already revoked token
    /// is still 204. Access tokens already issued stay valid until they expire.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Logout(LogoutRequest logoutRequest, CancellationToken cancellationToken)
    {
        var command = new LogoutCommand(logoutRequest.RefreshToken);

        var result = await sender.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return NoContent();
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
            return this.ToActionResult(result.Error!);
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
            return this.ToActionResult(result.Error!);
        }

        return Ok(result.Value);
    }
}
