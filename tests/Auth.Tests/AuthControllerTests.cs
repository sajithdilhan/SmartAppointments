using Auth.Api.Controllers;
using Auth.Application.Commands;
using Auth.Application.Models;
using Auth.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Reflection;
using Moq;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Enums;
using SmartAppointments.BuildingBlocks.Models;
using System.Security.Claims;

namespace Auth.Tests;

public class AuthControllerTests
{
    [Fact]
    public async Task Login_Successful_Returns_Ok()
    {
        // Arrange
        var mockSender = new Mock<ISender>();
        var loginRequest = new UserLoginRequest("test@example.com", "password");
        mockSender.Setup(s => s.Send(It.IsAny<LoginUserCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TokenResponse>.Success(new TokenResponse("access_token", "refresh_token", new DateTime(2026, 10, 4, 13, 0, 0, DateTimeKind.Utc))));

        var subject = new AuthController(mockSender.Object);

        // Act
        var result = await subject.Login(loginRequest);

        // Assert
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, (result as OkObjectResult)?.StatusCode);
    }

    [Fact]
    public async Task Login_Failure_Returns_Unauthorized()
    {
        // Arrange
        var mockSender = new Mock<ISender>();
        var loginRequest = new UserLoginRequest("invalid@example.com", "wrongpassword");
        mockSender.Setup(s => s.Send(It.IsAny<LoginUserCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TokenResponse>.Failure(new Error(401, "Invalid credentials")));

        var subject = new AuthController(mockSender.Object);

        // Act
        var result = await subject.Login(loginRequest);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, (result as UnauthorizedObjectResult)?.StatusCode);
    }

    [Fact]
    public async Task Register_Successful_Returns_Created()
    {
        // Arrange
        var mockSender = new Mock<ISender>();
        var registerRequest = new RegisterCustomerRequest("John", "Doe", "johndoe@example.com", "1234567890", "password");
        mockSender.Setup(s => s.Send(It.IsAny<RegisterCustomerCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RegisterCustomerResponse>.Success(new RegisterCustomerResponse(Guid.NewGuid(), "johndoe@example.com", UserRole.Customer.ToString())));
        var cancellationToken = new CancellationToken();
        var subject = new AuthController(mockSender.Object);

        // Act
        var result = await subject.Register(registerRequest, cancellationToken);

        // Assert
        Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(201, (result as CreatedAtActionResult)?.StatusCode);
    }

    [Fact]
    public async Task Register_Failure_Returns_BadRequest()
    {
        // Arrange
        var mockSender = new Mock<ISender>();
        var registerRequest = new RegisterCustomerRequest("John", "Doe", "johndoe@example.com", "1234567890", "password");
        mockSender.Setup(s => s.Send(It.IsAny<RegisterCustomerCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RegisterCustomerResponse>.Failure(new Error(400, "Invalid request")));

        var subject = new AuthController(mockSender.Object);

        // Act
        var result = await subject.Register(registerRequest, new CancellationToken());

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, (result as BadRequestObjectResult)?.StatusCode);
    }

    [Fact]
    public async Task GetProfile_Successful_Returns_Ok()
    {
        // Arrange
        var mockSender = new Mock<ISender>();
        var email = "test@example.com";

        mockSender.Setup(s => s.Send(It.IsAny<GetCustomerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<GetCustomerResponse?>.Success(new GetCustomerResponse("John", "Doe", email, "1234567890", true)));

        var subject = GetSubjectWithHttpContextUser(email, UserRole.Customer, mockSender);
        var cancellationToken = new CancellationToken();

        // Act
        var result = await subject.GetProfile(email, cancellationToken);

        // Assert
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, (result as OkObjectResult)?.StatusCode);
    }

    [Fact]
    public async Task GetProfile_Passes_Caller_Claims_To_The_Query()
    {
        // Guards the claim-name contract: the controller must read the same short claim names
        // the token is issued with, otherwise the handler receives nulls and denies every request.
        var mockSender = new Mock<ISender>();
        var email = "test@example.com";
        GetCustomerQuery? captured = null;

        mockSender.Setup(s => s.Send(It.IsAny<GetCustomerQuery>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((q, _) => captured = (GetCustomerQuery)q)
            .ReturnsAsync(Result<GetCustomerResponse?>.Success(new GetCustomerResponse("John", "Doe", email, "1234567890", true)));

        var subject = GetSubjectWithHttpContextUser(email, UserRole.Customer, mockSender);

        // Act
        await subject.GetProfile(email, CancellationToken.None);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal(email, captured!.CurrentUserEmail);
        Assert.Equal(UserRole.Customer.ToString(), captured.CurrentUserRole);
    }

    [Fact]
    public async Task GetProfile_NotFound_Returns_NotFound()
    {
        // Arrange
        var mockSender = new Mock<ISender>();
        var email = "missing@example.com";

        mockSender.Setup(s => s.Send(It.IsAny<GetCustomerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<GetCustomerResponse?>.Failure(new Error(404, "Customer not found")));

        var subject = GetSubjectWithHttpContextUser(email, UserRole.Customer, mockSender);

        // Act
        var result = await subject.GetProfile(email, CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(404, (result as NotFoundObjectResult)?.StatusCode);
    }

    [Fact]
    public async Task GetProfile_WithoutClaims_Returns_Forbidden()
    {
        // The handler denies with 403; the controller must report that status rather than
        // collapsing every failure into 404, which is what it used to do.
        var mockSender = new Mock<ISender>();

        mockSender.Setup(s => s.Send(It.IsAny<GetCustomerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<GetCustomerResponse?>.Failure(new Error(403, "Permission denied.")));

        var subject = new AuthController(mockSender.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
            }
        };

        // Act
        var result = await subject.GetProfile("test@example.com", CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetProfile_MalformedEmail_Returns_BadRequest()
    {
        var mockSender = new Mock<ISender>();

        mockSender.Setup(s => s.Send(It.IsAny<GetCustomerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<GetCustomerResponse?>.Failure(new Error(400, "A valid email is required.")));

        var subject = GetSubjectWithHttpContextUser("admin@example.com", UserRole.Admin, mockSender);

        // Act
        var result = await subject.GetProfile("not-an-email", CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, (result as BadRequestObjectResult)?.StatusCode);
    }

    [Fact]
    public async Task Login_ValidationFailure_Returns_BadRequest()
    {
        // A 400 from the validator used to be reported as 401 because Login returned a fixed
        // Unauthorized for every failure.
        var mockSender = new Mock<ISender>();
        var loginRequest = new UserLoginRequest("not-an-email", "");
        mockSender.Setup(s => s.Send(It.IsAny<LoginUserCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TokenResponse>.Failure(new Error(400, "Invalid request: Email is required.")));

        var subject = new AuthController(mockSender.Object);

        // Act
        var result = await subject.Login(loginRequest);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, (result as BadRequestObjectResult)?.StatusCode);
    }

    [Fact]
    public async Task GetMe_Resolves_The_Caller_From_The_Token()
    {
        // /me takes no email parameter at all: the requested address must come from the claims.
        var mockSender = new Mock<ISender>();
        var email = "caller@example.com";
        GetCustomerQuery? captured = null;

        mockSender.Setup(s => s.Send(It.IsAny<GetCustomerQuery>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((q, _) => captured = (GetCustomerQuery)q)
            .ReturnsAsync(Result<GetCustomerResponse?>.Success(new GetCustomerResponse("John", "Doe", email, "1234567890", true)));

        var subject = GetSubjectWithHttpContextUser(email, UserRole.Staff, mockSender);

        // Act
        var result = await subject.GetMe(CancellationToken.None);

        // Assert
        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(captured);
        Assert.Equal(email, captured!.RequestedEmail);
        Assert.Equal(email, captured.CurrentUserEmail);
        Assert.Equal(UserRole.Staff.ToString(), captured.CurrentUserRole);
    }

    [Fact]
    public async Task GetMe_WithoutClaims_Returns_Forbidden()
    {
        var mockSender = new Mock<ISender>();

        mockSender.Setup(s => s.Send(It.IsAny<GetCustomerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<GetCustomerResponse?>.Failure(new Error(403, "Permission denied.")));

        var subject = new AuthController(mockSender.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
            }
        };

        // Act
        var result = await subject.GetMe(CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, objectResult.StatusCode);
    }

    [Fact]
    public async Task Refresh_Successful_Returns_Ok_With_The_Response()
    {
        var response = new TokenResponse("new-access", "new-refresh", new DateTime(2026, 10, 4, 13, 0, 0, DateTimeKind.Utc));
        var mockSender = new Mock<ISender>();
        mockSender.Setup(s => s.Send(It.Is<RefreshTokenCommand>(c => c.RefreshToken == "old-refresh"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TokenResponse>.Success(response));
        var subject = new AuthController(mockSender.Object);

        var result = await subject.Refresh(new RefreshTokenRequest("old-refresh"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(400)]
    public async Task Refresh_Failure_Maps_Through_ToActionResult(int status)
    {
        var mockSender = new Mock<ISender>();
        mockSender.Setup(s => s.Send(It.IsAny<RefreshTokenCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TokenResponse>.Failure(new Error(status, "failed")));
        var subject = new AuthController(mockSender.Object);

        var result = await subject.Refresh(new RefreshTokenRequest("token"), CancellationToken.None);

        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Logout_Successful_Returns_NoContent()
    {
        var mockSender = new Mock<ISender>();
        mockSender.Setup(s => s.Send(It.Is<LogoutCommand>(c => c.RefreshToken == "token"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));
        var subject = new AuthController(mockSender.Object);

        var result = await subject.Logout(new LogoutRequest("token"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Logout_Failure_Returns_BadRequest()
    {
        var mockSender = new Mock<ISender>();
        mockSender.Setup(s => s.Send(It.IsAny<LogoutCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Failure(new Error(400, "Invalid request: Refresh token is required.")));
        var subject = new AuthController(mockSender.Object);

        var result = await subject.Logout(new LogoutRequest(null), CancellationToken.None);

        Assert.Equal(400, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Theory]
    [InlineData(nameof(AuthController.Refresh), "refresh")]
    [InlineData(nameof(AuthController.Logout), "logout")]
    public void Refresh_And_Logout_Are_Anonymous_Post_Routes(string action, string template)
    {
        var method = typeof(AuthController).GetMethod(action)!;

        Assert.NotNull(method.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(method.GetCustomAttribute<AuthorizeAttribute>());
        var http = Assert.Single(method.GetCustomAttributes<HttpMethodAttribute>());
        Assert.IsType<HttpPostAttribute>(http);
        Assert.Equal(template, http.Template);
    }

    //Set up a mock HttpContext with a user for testing purposes
    private AuthController GetSubjectWithHttpContextUser(string email, UserRole role, Mock<ISender> sender)
    {
       var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
        {
            new Claim(Constants.EmailClaimType, email),
            new Claim(Constants.RoleClaimType, role.ToString())
        }, "mock"));
        var httpContext = new DefaultHttpContext
        {
            User = user
        };
        var controllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        // Assign the controller context to the AuthController instance
        var subject = new AuthController(sender.Object);
        subject.ControllerContext = controllerContext;
        return subject;
    }
}
