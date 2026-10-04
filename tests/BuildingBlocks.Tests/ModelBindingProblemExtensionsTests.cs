using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using SmartAppointments.BuildingBlocks.Models;
using SmartAppointments.BuildingBlocks.Web.Results;

namespace BuildingBlocks.Tests;

public class ModelBindingProblemExtensionsTests
{
    private static ObjectResult Create(Action<ModelStateDictionary> arrange)
    {
        var context = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        arrange(context.ModelState);
        return Assert.IsType<ObjectResult>(ModelBindingProblemExtensions.CreateResponse(context));
    }

    [Fact]
    public void Joins_The_Messages_Into_A_400_Problem_Body()
    {
        var result = Create(m =>
        {
            m.AddModelError("Id", "The value 'x' is not valid.");
            m.AddModelError("Name", "The Name field is required.");
            m.AddModelError("Name", "");
        });

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("application/problem+json", result.ContentTypes);
        Assert.Equal(
            new ApiProblemDetails(400, "Invalid request data. Errors: The value 'x' is not valid.,The Name field is required."),
            result.Value);
    }

    [Fact]
    public void An_Exception_Without_A_Message_Gets_A_Fixed_Text_And_Is_Not_Echoed()
    {
        var result = Create(m => m.TryAddModelException("$", new InvalidOperationException("secret in Foo.cs line 9")));

        var problem = Assert.IsType<ApiProblemDetails>(result.Value);
        Assert.Equal(400, problem.Status);
        Assert.DoesNotContain("secret", problem.Detail);
        Assert.Contains(ModelBindingProblemExtensions.UnreadableBodyMessage, problem.Detail);
    }
}
