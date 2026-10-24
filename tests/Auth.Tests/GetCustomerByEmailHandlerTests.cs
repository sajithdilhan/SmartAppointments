using Auth.Application.Abstractions;
using Auth.Application.Handlers;
using Auth.Application.Queries;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartAppointments.BuildingBlocks.Enums;

namespace Auth.Tests;

public class GetCustomerByEmailHandlerTests
{
    [Fact]
    public async Task Profile_Read_Uses_The_Read_Only_Query()
    {
        // A profile lookup is a pure query; taking a tracked entity here would be wasted work.
        var repository = CreateRepository(CreateCustomer("cam@example.com"));
        var query = new GetCustomerQuery("cam@example.com", "cam@example.com", nameof(UserRole.Customer));

        await CreateSubject(repository).Handle(query, CancellationToken.None);

        repository.Verify(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.GetForUpdateByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Customer_Requesting_Another_Address_Gets_Their_Own_Profile()
    {
        var repository = CreateRepository(CreateCustomer("cam@example.com"));
        var query = new GetCustomerQuery("someone.else@example.com", "cam@example.com", nameof(UserRole.Customer));

        var result = await CreateSubject(repository).Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        repository.Verify(r => r.GetByEmailAsync("cam@example.com", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Admin_Reads_The_Requested_Address()
    {
        var repository = CreateRepository(CreateCustomer("cam@example.com"));
        var query = new GetCustomerQuery("cam@example.com", "admin@example.com", nameof(UserRole.Admin));

        var result = await CreateSubject(repository).Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        repository.Verify(r => r.GetByEmailAsync("cam@example.com", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Missing_Caller_Claims_Are_Denied()
    {
        var repository = CreateRepository(CreateCustomer("cam@example.com"));
        var query = new GetCustomerQuery("cam@example.com", null, null);

        var result = await CreateSubject(repository).Handle(query, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.Status);
    }

    [Fact]
    public async Task Unknown_Address_Returns_404()
    {
        var repository = CreateRepository(null);
        var query = new GetCustomerQuery("nobody@example.com", "admin@example.com", nameof(UserRole.Admin));

        var result = await CreateSubject(repository).Handle(query, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.Status);
    }

    [Fact]
    public async Task A_Deleted_Account_Returns_The_Same_404()
    {
        var deleted = User.RegisterStaff("Sam", "Staff", Email.Create("sam@example.com"), "+15551234567", "hash", Guid.NewGuid());
        deleted.MarkDeleted(new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc));
        var repository = CreateRepository(deleted);
        var query = new GetCustomerQuery("sam@example.com", "admin@example.com", nameof(UserRole.Admin));

        var result = await CreateSubject(repository).Handle(query, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.Status);
        Assert.Equal("Customer not found", result.Error.Details);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    public async Task Malformed_Requested_Email_Returns_400_Without_Querying(string email)
    {
        // These values reach Email.Create through the repository and throw ArgumentException,
        // which ExceptionMiddleware would report as a 500 for what is a caller mistake.
        var repository = CreateRepository(CreateCustomer("cam@example.com"));
        var query = new GetCustomerQuery(email, "admin@example.com", nameof(UserRole.Admin));

        var result = await CreateSubject(repository).Handle(query, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.Status);
        repository.Verify(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static GetCustomerByEmailHandler CreateSubject(Mock<IUserRepository> repository) =>
        new(repository.Object, NullLogger<GetCustomerByEmailHandler>.Instance);

    private static Mock<IUserRepository> CreateRepository(User? user)
    {
        var repository = new Mock<IUserRepository>();
        repository.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        return repository;
    }

    private static User CreateCustomer(string email) =>
        User.RegisterCustomer("Cam", "Customer", Email.Create(email), "+15551234567", "hash");
}
