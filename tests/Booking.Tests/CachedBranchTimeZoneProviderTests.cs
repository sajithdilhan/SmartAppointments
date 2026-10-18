using Booking.Application.Abstractions;
using Booking.Application.Models;
using Booking.Infrastructure.Clients;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Tests;

public class CachedBranchTimeZoneProviderTests
{
    private readonly Mock<IAvailabilityClient> _availability = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly Guid _branch = Guid.CreateVersion7();

    private CachedBranchTimeZoneProvider Provider() => new(_availability.Object, _cache);

    private void Returns(Result<BranchInfo?> result) =>
        _availability.Setup(a => a.GetBranchAsync(_branch, It.IsAny<CancellationToken>())).ReturnsAsync(result);

    [Fact]
    public async Task The_Zone_Is_Read_Once_And_Then_Served_From_The_Cache()
    {
        Returns(Result<BranchInfo?>.Success(new BranchInfo(_branch, true, "Pacific/Auckland")));
        var provider = Provider();

        var first = await provider.GetAsync(_branch, CancellationToken.None);
        var second = await provider.GetAsync(_branch, CancellationToken.None);

        Assert.Equal("Pacific/Auckland", first.Value);
        Assert.Equal("Pacific/Auckland", second.Value);
        _availability.Verify(a => a.GetBranchAsync(_branch, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Unknown_Branch_Gives_A_Null_Zone_And_Is_Cached()
    {
        Returns(Result<BranchInfo?>.Success(null));
        var provider = Provider();

        var first = await provider.GetAsync(_branch, CancellationToken.None);
        await provider.GetAsync(_branch, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Null(first.Value);
        _availability.Verify(a => a.GetBranchAsync(_branch, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Branch_Without_A_Schedule_Gives_A_Null_Zone()
    {
        Returns(Result<BranchInfo?>.Success(new BranchInfo(_branch, true, null)));

        var result = await Provider().GetAsync(_branch, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task A_Failure_Is_Returned_And_Never_Cached()
    {
        Returns(Result<BranchInfo?>.Failure(new Error(503, "down")));
        var provider = Provider();

        var first = await provider.GetAsync(_branch, CancellationToken.None);
        Assert.Equal(503, first.Error!.Status);

        Returns(Result<BranchInfo?>.Success(new BranchInfo(_branch, true, "America/New_York")));
        var second = await provider.GetAsync(_branch, CancellationToken.None);

        Assert.Equal("America/New_York", second.Value);
        _availability.Verify(a => a.GetBranchAsync(_branch, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Different_Branches_Are_Cached_Separately()
    {
        var other = Guid.CreateVersion7();
        Returns(Result<BranchInfo?>.Success(new BranchInfo(_branch, true, "Pacific/Auckland")));
        _availability.Setup(a => a.GetBranchAsync(other, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchInfo?>.Success(new BranchInfo(other, true, "America/New_York")));
        var provider = Provider();

        Assert.Equal("Pacific/Auckland", (await provider.GetAsync(_branch, CancellationToken.None)).Value);
        Assert.Equal("America/New_York", (await provider.GetAsync(other, CancellationToken.None)).Value);
    }
}
