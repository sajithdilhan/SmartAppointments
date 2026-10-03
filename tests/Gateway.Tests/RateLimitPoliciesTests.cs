using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using SmartAppointments.Gateway.RateLimiting;

namespace Gateway.Tests;

public class RateLimitPoliciesTests
{
    private static DefaultHttpContext Context(string? ip = "10.0.0.1", string? sub = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = ip is null ? null : IPAddress.Parse(ip);
        if (sub is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", sub)], "test"));
        }

        return context;
    }

    private static int Permitted(RateLimitPartition<string> partition, int attempts)
    {
        using var limiter = partition.Factory(partition.PartitionKey);
        var permitted = 0;
        for (var i = 0; i < attempts; i++)
        {
            using var lease = limiter.AttemptAcquire();
            if (lease.IsAcquired) permitted++;
        }

        return permitted;
    }

    [Fact]
    public void The_Constants_Equal_The_Business_Rules()
    {
        Assert.Equal(5, RateLimitPolicies.LoginPermits);
        Assert.Equal(10, RateLimitPolicies.AppointmentCreatePermits);
        Assert.Equal(30, RateLimitPolicies.SlotSearchPermits);
        Assert.Equal(TimeSpan.FromMinutes(1), RateLimitPolicies.Window);
        Assert.Equal(6, RateLimitPolicies.Segments);
        Assert.Equal("login", RateLimitPolicies.Login);
        Assert.Equal("appointment-create", RateLimitPolicies.AppointmentCreate);
        Assert.Equal("slot-search", RateLimitPolicies.SlotSearch);
    }

    [Fact]
    public void ByClientIp_Keys_On_The_Remote_Address()
    {
        Assert.Equal("ip:10.0.0.1", RateLimitPolicies.ByClientIp(Context("10.0.0.1")).PartitionKey);
        Assert.NotEqual(
            RateLimitPolicies.ByClientIp(Context("10.0.0.1")).PartitionKey,
            RateLimitPolicies.ByClientIp(Context("10.0.0.2")).PartitionKey);
    }

    [Fact]
    public void ByClientIp_Treats_An_IPv4_Mapped_IPv6_Address_As_The_IPv4_One()
    {
        Assert.Equal(
            RateLimitPolicies.ByClientIp(Context("10.0.0.1")).PartitionKey,
            RateLimitPolicies.ByClientIp(Context("::ffff:10.0.0.1")).PartitionKey);
    }

    [Fact]
    public void ByClientIp_Ignores_A_Spoofed_Forwarded_For_Header()
    {
        var spoofed = Context("10.0.0.1");
        spoofed.Request.Headers["X-Forwarded-For"] = "1.2.3.4";

        Assert.Equal("ip:10.0.0.1", RateLimitPolicies.ByClientIp(spoofed).PartitionKey);
    }

    [Fact]
    public void ByClientIp_Shares_One_Partition_When_The_Address_Is_Unknown()
    {
        Assert.Equal("ip:unknown", RateLimitPolicies.ByClientIp(Context(null)).PartitionKey);
    }

    [Fact]
    public void ByUser_Keys_On_The_Policy_And_The_Sub_Claim()
    {
        Assert.Equal("slot-search:u1", RateLimitPolicies.ByUser(Context(sub: "u1"), "slot-search", 30).PartitionKey);
        Assert.NotEqual(
            RateLimitPolicies.ByUser(Context(sub: "u1"), "slot-search", 30).PartitionKey,
            RateLimitPolicies.ByUser(Context(sub: "u2"), "slot-search", 30).PartitionKey);
        Assert.NotEqual(
            RateLimitPolicies.ByUser(Context(sub: "u1"), "slot-search", 30).PartitionKey,
            RateLimitPolicies.ByUser(Context(sub: "u1"), "appointment-create", 10).PartitionKey);
    }

    [Fact]
    public void ByUser_Falls_Back_To_The_Client_Address_Without_A_Sub()
    {
        Assert.Equal("ip:10.0.0.1", RateLimitPolicies.ByUser(Context("10.0.0.1"), "slot-search", 30).PartitionKey);
    }

    [Fact]
    public void Login_Permits_Exactly_Five_Attempts()
        => Assert.Equal(5, Permitted(RateLimitPolicies.ByClientIp(Context()), 20));

    [Fact]
    public void Appointment_Create_Permits_Exactly_Ten_Attempts()
        => Assert.Equal(10, Permitted(RateLimitPolicies.ByUser(Context(sub: "u1"), RateLimitPolicies.AppointmentCreate, RateLimitPolicies.AppointmentCreatePermits), 40));

    [Fact]
    public void Slot_Search_Permits_Exactly_Thirty_Attempts()
        => Assert.Equal(30, Permitted(RateLimitPolicies.ByUser(Context(sub: "u1"), RateLimitPolicies.SlotSearch, RateLimitPolicies.SlotSearchPermits), 60));

    [Fact]
    public void A_Sixth_Attempt_Is_Rejected_And_Any_Retry_After_Metadata_Is_At_Least_One_Second()
    {
        var partition = RateLimitPolicies.ByClientIp(Context());
        using var limiter = partition.Factory(partition.PartitionKey);
        for (var i = 0; i < 5; i++) limiter.AttemptAcquire().Dispose();

        using var rejected = limiter.AttemptAcquire();

        Assert.False(rejected.IsAcquired);
        // The sliding-window limiter of .NET 10 supplies no RetryAfter on a rejection; the rejection
        // writer then falls back to the window length. If a future runtime supplies it, it must be sane.
        if (rejected.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            Assert.True(retryAfter >= TimeSpan.FromSeconds(1));
        }
    }
}
