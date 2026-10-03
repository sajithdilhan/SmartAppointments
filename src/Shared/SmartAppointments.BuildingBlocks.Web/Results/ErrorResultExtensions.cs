using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartAppointments.BuildingBlocks.Models;

namespace SmartAppointments.BuildingBlocks.Web.Results;

public static class ErrorResultExtensions
{
    /// <summary>
    /// Every failure path in a controller goes through here, so the status a handler chose is the
    /// status the caller sees. Branching on IsSuccess alone once collapsed 403 into 404 and 400
    /// into 401 in Auth.
    /// </summary>
    public static ObjectResult ToActionResult(this ControllerBase controller, Error error) => error.Status switch
    {
        StatusCodes.Status400BadRequest => controller.BadRequest(error),
        StatusCodes.Status401Unauthorized => controller.Unauthorized(error),
        StatusCodes.Status403Forbidden => controller.StatusCode(StatusCodes.Status403Forbidden, error),
        StatusCodes.Status404NotFound => controller.NotFound(error),
        StatusCodes.Status409Conflict => controller.Conflict(error),
        StatusCodes.Status422UnprocessableEntity => controller.UnprocessableEntity(error),
        StatusCodes.Status503ServiceUnavailable => controller.StatusCode(StatusCodes.Status503ServiceUnavailable, error),
        _ => controller.StatusCode(StatusCodes.Status500InternalServerError, error)
    };
}
