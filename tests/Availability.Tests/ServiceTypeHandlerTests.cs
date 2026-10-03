using Availability.Application.Abstractions;
using Availability.Application.Commands;
using Availability.Application.Handlers;
using Availability.Application.Queries;
using Availability.Application.Validations;
using Availability.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartAppointments.BuildingBlocks;

namespace Availability.Tests;

public class ServiceTypeHandlerTests
{
    private static readonly CreateServiceTypeCommand ValidCreate = new(" inspect ", " Inspection ", null, 30);

    private readonly Mock<IServiceTypeRepository> _repository = new();

    [Fact]
    public async Task Create_Stages_Then_Commits_And_Looks_Up_The_Normalised_Code()
    {
        ServiceType? staged = null;
        _repository.Setup(r => r.AddAsync(It.IsAny<ServiceType>(), It.IsAny<CancellationToken>()))
            .Callback<ServiceType, CancellationToken>((s, _) => staged = s);

        var result = await CreateHandler().Handle(ValidCreate, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(staged!.Id, result.Value!.Id);
        Assert.Equal("INSPECT", result.Value.Code);
        _repository.Verify(r => r.ExistsByCodeAsync("INSPECT", It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_With_An_Existing_Code_Returns_409_Without_Writing()
    {
        _repository.Setup(r => r.ExistsByCodeAsync("INSPECT", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateHandler().Handle(ValidCreate, CancellationToken.None);

        Assert.Equal(409, result.Error!.Status);
        Assert.Equal("A service type with code 'INSPECT' already exists.", result.Error.Details);
        _repository.Verify(r => r.AddAsync(It.IsAny<ServiceType>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_That_Loses_A_Race_At_Commit_Returns_409_Not_500()
    {
        _repository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateServiceTypeCodeException("duplicate"));

        var result = await CreateHandler().Handle(ValidCreate, CancellationToken.None);

        Assert.Equal(409, result.Error!.Status);
    }

    [Fact]
    public async Task Create_With_Invalid_Input_Returns_400_Without_Touching_The_Repository()
    {
        var result = await CreateHandler().Handle(ValidCreate with { DurationMinutes = 7 }, CancellationToken.None);

        Assert.Equal(400, result.Error!.Status);
        Assert.Contains("multiple of 5", result.Error.Details);
        _repository.Verify(r => r.ExistsByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Replaces_The_Details_And_Commits()
    {
        var serviceType = ServiceType.Create("INSPECT", "Inspection", null, 30);
        _repository.Setup(r => r.GetForUpdateByIdAsync(serviceType.Id, It.IsAny<CancellationToken>())).ReturnsAsync(serviceType);

        var result = await UpdateHandler().Handle(
            new UpdateServiceTypeCommand(serviceType.Id, "Full inspection", "Bring the logbook", 45), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(45, result.Value!.DurationMinutes);
        Assert.Equal("INSPECT", result.Value.Code);
        Assert.NotNull(result.Value.UpdatedAtUtc);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Of_An_Unknown_Service_Type_Returns_404()
    {
        var id = Guid.CreateVersion7();

        var result = await UpdateHandler().Handle(new UpdateServiceTypeCommand(id, "Inspection", null, 30), CancellationToken.None);

        Assert.Equal(404, result.Error!.Status);
        Assert.Equal($"Service type '{id}' was not found.", result.Error.Details);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_With_Invalid_Input_Returns_400_Without_Loading()
    {
        var result = await UpdateHandler().Handle(new UpdateServiceTypeCommand(Guid.CreateVersion7(), "", null, 0), CancellationToken.None);

        Assert.Equal(400, result.Error!.Status);
        _repository.Verify(r => r.GetForUpdateByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task SetActive_Commits_Only_When_The_State_Changes(bool startActive, bool requested, bool expectWrite)
    {
        var serviceType = ServiceType.Create("INSPECT", "Inspection", null, 30);
        if (!startActive)
        {
            serviceType.Deactivate();
        }
        _repository.Setup(r => r.GetForUpdateByIdAsync(serviceType.Id, It.IsAny<CancellationToken>())).ReturnsAsync(serviceType);

        var result = await SetActiveHandler().Handle(new SetServiceTypeActiveCommand(serviceType.Id, requested), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(requested, serviceType.IsActive);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), expectWrite ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task SetActive_On_An_Unknown_Service_Type_Returns_404()
    {
        var result = await SetActiveHandler().Handle(new SetServiceTypeActiveCommand(Guid.CreateVersion7(), true), CancellationToken.None);

        Assert.Equal(404, result.Error!.Status);
    }

    [Theory]
    [InlineData(Constants.AdminRole, true, true)]
    [InlineData(Constants.AdminRole, false, false)]
    [InlineData(Constants.StaffRole, true, false)]
    [InlineData(Constants.CustomerRole, true, false)]
    public async Task Only_An_Admin_Who_Asks_Lists_Inactive_Service_Types(string role, bool includeInactive, bool expected)
    {
        _repository.Setup(r => r.ListAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await new GetServiceTypesHandler(_repository.Object)
            .Handle(new GetServiceTypesQuery(includeInactive, role), CancellationToken.None);

        _repository.Verify(r => r.ListAsync(expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_List_Keeps_The_Repository_Order()
    {
        var b = ServiceType.Create("B", "Beta", null, 30);
        var a = ServiceType.Create("A1", "Alpha", null, 30);
        _repository.Setup(r => r.ListAsync(false, It.IsAny<CancellationToken>())).ReturnsAsync([a, b]);

        var result = await new GetServiceTypesHandler(_repository.Object)
            .Handle(new GetServiceTypesQuery(false, Constants.CustomerRole), CancellationToken.None);

        Assert.Equal([a.Id, b.Id], result.Value!.Select(s => s.Id));
    }

    [Theory]
    [InlineData(Constants.AdminRole, true, true)]
    [InlineData(Constants.CustomerRole, true, true)]
    [InlineData(Constants.AdminRole, false, true)]
    [InlineData(Constants.StaffRole, false, false)]
    [InlineData(Constants.CustomerRole, false, false)]
    public async Task Get_Hides_An_Inactive_Service_Type_From_Non_Admins(string role, bool isActive, bool visible)
    {
        var serviceType = ServiceType.Create("INSPECT", "Inspection", null, 30);
        if (!isActive)
        {
            serviceType.Deactivate();
        }
        _repository.Setup(r => r.GetByIdAsync(serviceType.Id, It.IsAny<CancellationToken>())).ReturnsAsync(serviceType);

        var result = await new GetServiceTypeHandler(_repository.Object)
            .Handle(new GetServiceTypeQuery(serviceType.Id, role), CancellationToken.None);

        Assert.Equal(visible, result.IsSuccess);
        if (!visible)
        {
            // The same 404 and message as a service type that does not exist at all.
            Assert.Equal(ServiceTypeNotFound(serviceType.Id), result.Error);
        }
        _repository.Verify(r => r.GetForUpdateByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static SmartAppointments.BuildingBlocks.Models.Error ServiceTypeNotFound(Guid id) =>
        new(404, $"Service type '{id}' was not found.");

    private CreateServiceTypeCommandHandler CreateHandler() =>
        new(_repository.Object, new CreateServiceTypeCommandValidator(), NullLogger<CreateServiceTypeCommandHandler>.Instance);

    private UpdateServiceTypeCommandHandler UpdateHandler() =>
        new(_repository.Object, new UpdateServiceTypeCommandValidator(), NullLogger<UpdateServiceTypeCommandHandler>.Instance);

    private SetServiceTypeActiveCommandHandler SetActiveHandler() =>
        new(_repository.Object, NullLogger<SetServiceTypeActiveCommandHandler>.Instance);
}
