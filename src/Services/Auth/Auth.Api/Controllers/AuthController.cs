using Auth.Application.Commands;
using Auth.Application.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Auth.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController(ISender sender) : ControllerBase
{
    [HttpPost("login")]
    public IActionResult Login()
    {
        // Login logic here
        return Ok();
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
    public IActionResult GetProfile()
    {
        // Profile retrieval logic here
        return Ok();
    }
}
