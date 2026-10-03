using Availability.Application.Abstractions;
using Availability.Application.Handlers;
using Availability.Application.Queries;
using Availability.Domain.Entities;
using Moq;

namespace Availability.Tests;

public class GetInternalSlotHandlerTests
{
    private readonly Mock<ISlotRepository> _slots = new();

    [Fact]
    public async Task A_Missing_Slot_Returns_404()
    {
        var id = Guid.CreateVersion7();

        var result = await new GetInternalSlotHandler(_slots.Object).Handle(new GetInternalSlotQuery(id), CancellationToken.None);

        Assert.Equal(404, result.Error!.Status);
        Assert.Equal($"Slot '{id}' was not found.", result.Error.Details);
    }

    [Fact]
    public async Task An_Existing_Slot_Is_Returned_With_Its_Window_And_Counts()
    {
        var slot = Slot.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), new DateOnly(2030, 1, 7),
            new DateTime(2030, 1, 7, 3, 30, 0, DateTimeKind.Utc), new DateTime(2030, 1, 7, 4, 0, 0, DateTimeKind.Utc), 5);
        _slots.Setup(r => r.GetByIdAsync(slot.Id, It.IsAny<CancellationToken>())).ReturnsAsync(slot);

        var result = await new GetInternalSlotHandler(_slots.Object).Handle(new GetInternalSlotQuery(slot.Id), CancellationToken.None);

        var response = result.Value!;
        Assert.Equal(slot.Id, response.Id);
        Assert.Equal(slot.BranchId, response.BranchId);
        Assert.Equal(slot.ServiceTypeId, response.ServiceTypeId);
        Assert.Equal(slot.StartUtc, response.StartUtc);
        Assert.Equal(slot.EndUtc, response.EndUtc);
        Assert.Equal(5, response.Capacity);
        Assert.Equal(0, response.ReservedCount);
        Assert.Equal(5, response.AvailableCapacity);
    }
}
