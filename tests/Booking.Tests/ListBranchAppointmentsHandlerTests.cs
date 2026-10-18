using Booking.Application.Abstractions;
using Booking.Application.Handlers;
using Booking.Application.Models;
using Booking.Application.Queries;
using Booking.Application.Validations;
using Booking.Domain.Entities;
using Moq;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Tests;

public class ListBranchAppointmentsHandlerTests
{
    // 2030-01-06 12:00Z is 2030-01-07 01:00 in Auckland and 2030-01-06 07:00 in New York.
    private static readonly DateTimeOffset Now = new(2030, 1, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IBranchTimeZoneProvider> _zones = new();
    private readonly Mock<IAuthClient> _auth = new();
    private readonly Guid _branch = Guid.CreateVersion7();

    public ListBranchAppointmentsHandlerTests()
    {
        _zones.Setup(z => z.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string?>.Success("America/New_York"));
        SetupPage([], 0);
        _auth.Setup(a => a.LookupAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyDictionary<Guid, CustomerInfo>>.Success(new Dictionary<Guid, CustomerInfo>()));
    }

    private void SetupPage(IReadOnlyList<Appointment> items, int total) =>
        _appointments.Setup(r => r.ListForBranchAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<AppointmentStatus?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppointmentPage(items, total));

    private Appointment Book(Guid customerId) => Appointment.Book(
        Guid.CreateVersion7(), customerId, Guid.CreateVersion7(), _branch, Guid.CreateVersion7(),
        Now.UtcDateTime, Now.UtcDateTime.AddMinutes(30), Now.UtcDateTime.AddDays(-2));

    private Task<Result<BranchAppointmentsPage>> Send(
        string role = Constants.AdminRole, Guid? callerBranch = null, Guid? branch = null,
        string? date = null, string? status = null, string? page = null, string? pageSize = null) =>
        new ListBranchAppointmentsHandler(
            _appointments.Object, _zones.Object, _auth.Object, new ListBranchAppointmentsQueryValidator(), new FixedTimeProvider(Now))
            .Handle(new ListBranchAppointmentsQuery(branch ?? _branch, date, status, page, pageSize, role, callerBranch),
                CancellationToken.None);

    [Fact]
    public async Task Staff_Of_Another_Branch_Gets_403_And_Nothing_Is_Read()
    {
        var result = await Send(Constants.StaffRole, Guid.CreateVersion7());

        Assert.Equal(403, result.Error!.Status);
        Assert.Equal("You can only view your own branch's appointments.", result.Error.Details);
        _zones.VerifyNoOtherCalls();
        _appointments.VerifyNoOtherCalls();
        _auth.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Staff_Without_A_Branch_Claim_Gets_403()
    {
        var result = await Send(Constants.StaffRole, null);

        Assert.Equal(403, result.Error!.Status);
    }

    [Fact]
    public async Task Staff_Of_The_Branch_Is_Allowed()
    {
        var result = await Send(Constants.StaffRole, _branch);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Admin_Can_Read_Any_Branch()
    {
        var result = await Send(Constants.AdminRole, null, Guid.CreateVersion7());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Validation_Fails_Before_Any_Call_Even_For_Staff_Of_Another_Branch()
    {
        var result = await Send(Constants.StaffRole, Guid.CreateVersion7(), date: "bad", page: "0");

        Assert.Equal(400, result.Error!.Status);
        Assert.StartsWith("Invalid request data. Errors: ", result.Error.Details);
        Assert.Contains("yyyy-MM-dd", result.Error.Details);
        Assert.Contains("page", result.Error.Details);
        _zones.VerifyNoOtherCalls();
        _appointments.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_Default_Date_Is_Today_In_The_Branch_Zone()
    {
        _zones.Setup(z => z.GetAsync(_branch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string?>.Success("Pacific/Auckland"));

        await Send();

        // 2030-01-07 in Auckland (+13): 2030-01-06 11:00Z to 2030-01-07 11:00Z.
        _appointments.Verify(r => r.ListForBranchAsync(
            _branch, new DateTime(2030, 1, 6, 11, 0, 0, DateTimeKind.Utc), new DateTime(2030, 1, 7, 11, 0, 0, DateTimeKind.Utc),
            null, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Explicit_Date_Is_Used_In_The_Branch_Zone_With_Filters_And_Paging()
    {
        await Send(date: "2030-03-10", status: "completed", page: "3", pageSize: "50");

        _appointments.Verify(r => r.ListForBranchAsync(
            _branch, new DateTime(2030, 3, 10, 5, 0, 0, DateTimeKind.Utc), new DateTime(2030, 3, 11, 4, 0, 0, DateTimeKind.Utc),
            AppointmentStatus.Completed, 3, 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Null_Zone_Falls_Back_To_Utc()
    {
        _zones.Setup(z => z.GetAsync(_branch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string?>.Success(null));

        await Send(date: "2030-03-10");

        _appointments.Verify(r => r.ListForBranchAsync(
            _branch, new DateTime(2030, 3, 10, 0, 0, 0, DateTimeKind.Utc), new DateTime(2030, 3, 11, 0, 0, 0, DateTimeKind.Utc),
            null, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Zone_Failure_Is_503_And_Nothing_Is_Listed()
    {
        _zones.Setup(z => z.GetAsync(_branch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string?>.Failure(new Error(503, "down")));

        var result = await Send();

        Assert.Equal(503, result.Error!.Status);
        _appointments.VerifyNoOtherCalls();
        _auth.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Customer_Names_Are_Merged_And_Unknown_Customers_Are_Null()
    {
        var known = Guid.CreateVersion7();
        var unknown = Guid.CreateVersion7();
        SetupPage([Book(known), Book(unknown), Book(known)], 7);
        IReadOnlyCollection<Guid>? asked = null;
        _auth.Setup(a => a.LookupAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, CancellationToken>((ids, _) => asked = ids)
            .ReturnsAsync(Result<IReadOnlyDictionary<Guid, CustomerInfo>>.Success(
                new Dictionary<Guid, CustomerInfo> { [known] = new("Ada", "Lovelace", "0412") }));

        var result = await Send(page: "2", pageSize: "3");

        Assert.True(result.IsSuccess);
        var page = result.Value!;
        Assert.Equal(2, page.Page);
        Assert.Equal(3, page.PageSize);
        Assert.Equal(7, page.TotalCount);
        Assert.False(page.CustomersUnavailable);
        Assert.Equal(new CustomerSummary("Ada", "Lovelace", "0412"), page.Items[0].Customer);
        Assert.Null(page.Items[1].Customer);
        Assert.Equal(known, page.Items[2].CustomerId);
        Assert.Equal(2, asked!.Count);
    }

    [Fact]
    public async Task An_Auth_Failure_Gives_The_Page_With_No_Names_And_The_Flag()
    {
        var customer = Guid.CreateVersion7();
        SetupPage([Book(customer)], 1);
        _auth.Setup(a => a.LookupAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyDictionary<Guid, CustomerInfo>>.Failure(new Error(503, "down")));

        var result = await Send();

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.CustomersUnavailable);
        Assert.Single(result.Value.Items);
        Assert.Null(result.Value.Items[0].Customer);
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task An_Empty_Page_Makes_No_Lookup()
    {
        var result = await Send();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.False(result.Value.CustomersUnavailable);
        _auth.VerifyNoOtherCalls();
    }
}
