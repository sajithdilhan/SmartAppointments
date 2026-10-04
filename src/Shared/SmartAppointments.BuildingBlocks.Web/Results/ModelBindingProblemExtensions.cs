using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using SmartAppointments.BuildingBlocks.Models;

namespace SmartAppointments.BuildingBlocks.Web.Results;

public static class ModelBindingProblemExtensions
{
    public const string InvalidRequestPrefix = "Invalid request data. Errors: ";
    public const string UnreadableBodyMessage = "The request body could not be read.";

    /// <summary>
    /// Makes the 400 that <c>[ApiController]</c> sends for a bad body, route or query value use the same
    /// <c>{status, detail}</c> problem+json body as every other error, instead of a ValidationProblemDetails.
    /// </summary>
    public static IMvcBuilder AddSharedProblemDetails(this IMvcBuilder builder)
        => builder.ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = CreateResponse);

    public static IActionResult CreateResponse(ActionContext context)
    {
        var messages = new List<string>();
        foreach (var entry in context.ModelState.Values)
        {
            foreach (var error in entry.Errors)
            {
                // An exception's text can carry internals (paths, types, offsets), so it is never echoed.
                var message = !string.IsNullOrWhiteSpace(error.ErrorMessage) ? error.ErrorMessage
                    : error.Exception is not null ? UnreadableBodyMessage
                    : null;
                if (message is not null && !messages.Contains(message))
                {
                    messages.Add(message);
                }
            }
        }

        var detail = messages.Count > 0 ? InvalidRequestPrefix + string.Join(",", messages) : UnreadableBodyMessage;
        var result = new ObjectResult(new ApiProblemDetails(StatusCodes.Status400BadRequest, detail))
        {
            StatusCode = StatusCodes.Status400BadRequest
        };
        result.ContentTypes.Add(ProblemDetailsWriter.ContentType);
        return result;
    }
}
