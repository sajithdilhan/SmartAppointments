using Auth.Domain.Entities;

namespace Auth.Tests;

/// <summary>
/// Puts a user into the state the activate/delete race can leave: deleted, yet <c>IsActive</c> true
/// (the domain methods refuse to produce it, so the setter is reached by reflection).
/// </summary>
internal static class DeletedUserHelper
{
    public static void ForceActive(User user) =>
        typeof(User).GetProperty(nameof(User.IsActive))!.SetValue(user, true);
}
