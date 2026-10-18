using Auth.Application.Abstractions;
using Auth.Application.Commands;
using Auth.Application.Handlers;
using Auth.Application.Validations;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartAppointments.BuildingBlocks.Enums;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Tests;

public class CreateStaffHandlerTests
{
    private static readonly Guid BranchId = Guid.CreateVersion7();

    private static readonly CreateStaffCommand ValidCommand =
        new("Sam", "Staff", "sam@example.com", "+15551234567", "P@ssw0rd!23", BranchId);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IAvailabilityClient> _availability = new();
    private readonly List<string> _calls = [];

    public CreateStaffHandlerTests()
    {
        _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("email"))
            .ReturnsAsync((User?)null);
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");
        _availability.Setup(a => a.GetBranchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("availability"))
            .ReturnsAsync(Result<BranchInfo?>.Success(new BranchInfo(BranchId, true, "UTC")));
    }

    [Fact]
    public async Task A_Valid_Command_Creates_An_Active_Staff_User_With_The_Branch()
    {
        User? added = null;
        _users.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => added = u);

        var result = await CreateSubject().Handle(ValidCommand, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Equal(UserRole.Staff, added.Role);
        Assert.Equal(BranchId, added.BranchId);
        Assert.True(added.IsActive);
        Assert.Equal("hashed", added.PasswordHash);
        Assert.Equal(added.Id, result.Value!.Id);
        Assert.Equal("Sam", result.Value.FirstName);
        Assert.Equal("Staff", result.Value.LastName);
        Assert.Equal("sam@example.com", result.Value.Email);
        Assert.Equal("+15551234567", result.Value.PhoneNumber);
        Assert.Equal("Staff", result.Value.Role);
        Assert.Equal(BranchId, result.Value.BranchId);
        _hasher.Verify(h => h.Hash("P@ssw0rd!23"), Times.Once);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_Branch_Is_Looked_Up_By_Id()
    {
        await CreateSubject().Handle(ValidCommand, CancellationToken.None);

        _availability.Verify(a => a.GetBranchAsync(BranchId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(InvalidCommands))]
    public async Task Every_Validation_Rule_Gives_400_And_Skips_Everything_Else(CreateStaffCommand command)
    {
        var result = await CreateSubject().Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.Status);
        Assert.StartsWith("Invalid request data. Errors: ", result.Error.Details);
        _users.Verify(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _availability.Verify(a => a.GetBranchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public static TheoryData<CreateStaffCommand> InvalidCommands => new()
    {
        ValidCommand with { FirstName = "" },
        ValidCommand with { LastName = new string('a', 51) },
        ValidCommand with { Email = "not-an-email" },
        ValidCommand with { PhoneNumber = "abc" },
        ValidCommand with { Password = "weak" },
        ValidCommand with { BranchId = Guid.Empty }
    };

    [Fact]
    public async Task Every_Failed_Rule_Is_Named_In_One_Detail()
    {
        var result = await CreateSubject().Handle(
            ValidCommand with { Email = "x", BranchId = Guid.Empty }, CancellationToken.None);

        Assert.Contains("Invalid email format.", result.Error!.Details);
        Assert.Contains("Branch is required.", result.Error.Details);
    }

    [Fact]
    public async Task An_Existing_Email_Is_400_Before_Availability_Is_Called()
    {
        _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(User.RegisterCustomer("Cam", "Customer", Email.Create("sam@example.com"), "+15551234567", "hash"));

        var result = await CreateSubject().Handle(ValidCommand, CancellationToken.None);

        Assert.Equal(400, result.Error!.Status);
        Assert.Equal("Email is already registered.", result.Error.Details);
        _availability.Verify(a => a.GetBranchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_Email_Check_Comes_Before_The_Availability_Call()
    {
        await CreateSubject().Handle(ValidCommand, CancellationToken.None);

        Assert.Equal(["email", "availability"], _calls);
    }

    [Fact]
    public async Task A_Duplicate_Found_Only_At_Commit_Is_The_Same_400()
    {
        _users.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateEmailException("Email is already registered."));

        var result = await CreateSubject().Handle(ValidCommand, CancellationToken.None);

        Assert.Equal(400, result.Error!.Status);
        Assert.Equal("Email is already registered.", result.Error.Details);
    }

    [Fact]
    public async Task An_Unknown_Branch_Is_422_And_Nothing_Is_Created()
    {
        _availability.Setup(a => a.GetBranchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchInfo?>.Success(null));

        var result = await CreateSubject().Handle(ValidCommand, CancellationToken.None);

        Assert.Equal(422, result.Error!.Status);
        Assert.Equal($"Branch '{BranchId}' does not exist or is inactive.", result.Error.Details);
        _users.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Inactive_Branch_Is_422_And_Nothing_Is_Created()
    {
        _availability.Setup(a => a.GetBranchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchInfo?>.Success(new BranchInfo(BranchId, false, null)));

        var result = await CreateSubject().Handle(ValidCommand, CancellationToken.None);

        Assert.Equal(422, result.Error!.Status);
        _users.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Unreachable_Availability_Is_503_And_Nothing_Is_Created()
    {
        _availability.Setup(a => a.GetBranchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchInfo?>.Failure(new Error(503, "The availability service is unavailable. Try again later.")));

        var result = await CreateSubject().Handle(ValidCommand, CancellationToken.None);

        Assert.Equal(503, result.Error!.Status);
        _users.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private CreateStaffHandler CreateSubject() => new(
        _users.Object,
        _hasher.Object,
        _availability.Object,
        new CreateStaffCommandValidator(),
        NullLogger<CreateStaffHandler>.Instance);
}
