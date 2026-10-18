using SmartAppointments.BuildingBlocks.Enums;

namespace SmartAppointments.BuildingBlocks;

public static class Constants
{
    public const string RoleClaimType = "role";
    public const string EmailClaimType = "email";
    public const string UserIdClaimType = "sub";
    /// <summary>The branch a Staff member works at, as a GUID string; only Staff tokens carry it.</summary>
    public const string BranchIdClaimType = "branch_id";
    public const string AdminRole = nameof(UserRole.Admin);
    public const string StaffRole = nameof(UserRole.Staff);
    public const string CustomerRole = nameof(UserRole.Customer);
    public const string AdminPolicy = "AdminPolicy";
    public const string CustomerPolicy = "CustomerPolicy";
    public const string StaffPolicy = "StaffPolicy";
    public const string AdminOrStaffPolicy = "AdminOrStaffPolicy";
    public const string AllowedOriginsPolicy = "AllowedOriginsPolicy";
    public const string InternalServicePolicy = "InternalServicePolicy";
    public const string ApiKeyAuthenticationScheme = "ApiKeyScheme";
    public const string ApiKeyHeaderName = "X-API-Key";
    public const string CorrelationIdHeaderName = "X-Correlation-ID";
}
