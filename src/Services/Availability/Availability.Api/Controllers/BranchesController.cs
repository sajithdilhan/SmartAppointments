using Availability.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Availability.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class BranchesController(ISender sender) : ControllerBase
{
    [HttpGet]

    public async Task<IActionResult> GetBranches(CancellationToken cancellationToken)
    {
        var query = new GetBranchesQuery();
        var result = await sender.Send(query, cancellationToken);
        if (!result.IsSuccess)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }
}
