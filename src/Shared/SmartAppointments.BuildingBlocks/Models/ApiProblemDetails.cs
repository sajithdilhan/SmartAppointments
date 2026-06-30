namespace SmartAppointments.BuildingBlocks.Models;

public sealed record ApiProblemDetails(
    int Status,
    string? Detail = null);

