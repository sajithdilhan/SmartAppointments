using Auth.Api.Controllers;
using Auth.Application.Commands;
using Auth.Application.Models;
using Auth.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
            .ReturnsAsync(Result<TokenResponse>.Success(new TokenResponse("access_token", "refresh_token")));

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
    public async Task GetProfile_WithoutClaims_Returns_NotFound()
    {
        // Arrange
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
        Assert.IsType<NotFoundObjectResult>(result);
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
