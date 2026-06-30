using Auth.Application.Commands;
using Auth.Application.Models;
using Auth.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

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
            return Unauthorized(result.Error);
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
            return BadRequest(result.Error);
        }

        return CreatedAtAction(
            nameof(Register),
            new { id = result.Value!.UserId },
            result.Value);
    }

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(string email, CancellationToken cancellationToken)
    {
        var query = new GetCustomerQuery(email);
        var result = await sender.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return NotFound(result.Error);
        }

        return Ok(result.Value);
    }
}
