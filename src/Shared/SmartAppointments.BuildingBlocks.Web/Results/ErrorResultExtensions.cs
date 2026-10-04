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
        StatusCodes.Status400BadRequest => WithProblemBody(controller.BadRequest(error), error),
        StatusCodes.Status401Unauthorized => WithProblemBody(controller.Unauthorized(error), error),
        StatusCodes.Status403Forbidden => WithProblemBody(controller.StatusCode(StatusCodes.Status403Forbidden, error), error),
        StatusCodes.Status404NotFound => WithProblemBody(controller.NotFound(error), error),
        StatusCodes.Status409Conflict => WithProblemBody(controller.Conflict(error), error),
        StatusCodes.Status422UnprocessableEntity => WithProblemBody(controller.UnprocessableEntity(error), error),
        StatusCodes.Status503ServiceUnavailable => WithProblemBody(controller.StatusCode(StatusCodes.Status503ServiceUnavailable, error), error),
        _ => WithProblemBody(controller.StatusCode(StatusCodes.Status500InternalServerError, error), error)
    };

    // The same {status, detail} body the middleware and the gateway write. The body carries the status
    // the response actually has, so an unmapped one reads 500 in both places.
    private static ObjectResult WithProblemBody(ObjectResult result, Error error)
    {
        result.Value = new ApiProblemDetails(result.StatusCode ?? StatusCodes.Status500InternalServerError, error.Details);
        result.ContentTypes.Add(ProblemDetailsWriter.ContentType);
        return result;
    }
}
