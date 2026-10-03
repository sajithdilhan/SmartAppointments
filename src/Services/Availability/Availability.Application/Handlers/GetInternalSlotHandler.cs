using Availability.Application.Abstractions;
using Availability.Application.Models;
using Availability.Application.Queries;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

public class GetInternalSlotHandler(ISlotRepository slotRepository)
    : IRequestHandler<GetInternalSlotQuery, Result<InternalSlotResponse>>
{
    public async Task<Result<InternalSlotResponse>> Handle(GetInternalSlotQuery request, CancellationToken cancellationToken)
    {
        var slot = await slotRepository.GetByIdAsync(request.Id, cancellationToken);
        if (slot is null)
        {
            return Result<InternalSlotResponse>.Failure(SlotErrors.NotFound(request.Id));
        }

        return Result<InternalSlotResponse>.Success(InternalSlotResponse.From(slot));
    }
}
