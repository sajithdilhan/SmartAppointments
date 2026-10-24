using Auth.Application.Abstractions;
using Auth.Application.Handlers;
using Auth.Application.Queries;
using Auth.Application.Validations;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Auth.Tests;

public class LookupUsersHandlerTests
{
    private readonly Mock<IUserRepository> _users = new();

    [Fact]
    public async Task Known_Users_Are_Returned_With_Display_Data_Only()
    {
        var cam = User.RegisterCustomer("Cam", "Customer", Email.Create("cam@example.com"), "+15551234567", "hash");
        var sam = User.RegisterStaff("Sam", "Staff", Email.Create("sam@example.com"), "+15557654321", "hash", Guid.NewGuid());
        _users.Setup(r => r.ListByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([cam, sam]);

        var result = await CreateSubject().Handle(new LookupUsersQuery([cam.Id, sam.Id]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        var item = result.Value.Single(i => i.Id == cam.Id);
        Assert.Equal("Cam", item.FirstName);
        Assert.Equal("Customer", item.LastName);
        Assert.Equal("+15551234567", item.PhoneNumber);
        // The item type has exactly these four properties, so no email or role can leak.
        Assert.Equal(["Id", "FirstName", "LastName", "PhoneNumber"], typeof(Auth.Application.Models.UserLookupItem)
            .GetProperties().Select(p => p.Name).Where(n => n != "EqualityContract"));
    }

    [Fact]
    public async Task Unknown_Ids_Are_Simply_Absent()
    {
        var known = User.RegisterCustomer("Cam", "Customer", Email.Create("cam@example.com"), "+15551234567", "hash");
        _users.Setup(r => r.ListByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([known]);

        var result = await CreateSubject().Handle(new LookupUsersQuery([known.Id, Guid.NewGuid()]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
    }

    [Fact]
    public async Task A_Deleted_User_Is_Still_Returned()
    {
        // Booking still shows the names on a deleted staff member's past appointments.
        var sam = User.RegisterStaff("Sam", "Staff", Email.Create("sam@example.com"), "+15557654321", "hash", Guid.NewGuid());
        sam.MarkDeleted(new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc));
        _users.Setup(r => r.ListByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([sam]);

        var result = await CreateSubject().Handle(new LookupUsersQuery([sam.Id]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Sam", Assert.Single(result.Value!).FirstName);
    }

    [Fact]
    public async Task Duplicate_Ids_Are_Looked_Up_Once()
    {
        var id = Guid.NewGuid();
        IReadOnlyCollection<Guid>? asked = null;
        _users.Setup(r => r.ListByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, CancellationToken>((ids, _) => asked = ids)
            .ReturnsAsync([]);

        await CreateSubject().Handle(new LookupUsersQuery([id, id, id]), CancellationToken.None);

        Assert.Equal([id], asked);
    }

    [Fact]
    public async Task An_Invalid_Query_Is_400_Without_A_Repository_Call()
    {
        var result = await CreateSubject().Handle(new LookupUsersQuery(null), CancellationToken.None);

        Assert.Equal(400, result.Error!.Status);
        Assert.StartsWith("Invalid request data. Errors: ", result.Error.Details);
        _users.Verify(r => r.ListByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private LookupUsersHandler CreateSubject() => new(
        _users.Object, new LookupUsersQueryValidator(), NullLogger<LookupUsersHandler>.Instance);
}
