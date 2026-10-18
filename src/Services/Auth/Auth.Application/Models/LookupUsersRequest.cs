namespace Auth.Application.Models;

public sealed record LookupUsersRequest(List<Guid>? Ids);
