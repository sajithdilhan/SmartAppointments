namespace Availability.Application.Models;

public sealed record GenerateSlotsRequest(Guid BranchId, Guid ServiceTypeId, DateOnly FromDate, DateOnly ToDate, int Capacity);
