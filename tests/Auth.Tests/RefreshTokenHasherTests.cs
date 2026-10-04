using Auth.Infrastructure.Services;

namespace Auth.Tests;

public class RefreshTokenHasherTests
{
    private readonly RefreshTokenHasher _subject = new();

    [Fact]
    public void Hash_Is_Deterministic()
    {
        Assert.Equal(_subject.Hash("some-token"), _subject.Hash("some-token"));
    }

    [Fact]
    public void Hash_Is_64_Lower_Case_Hex_Characters()
    {
        var hash = _subject.Hash("some-token");

        Assert.Equal(64, hash.Length);
        Assert.All(hash, c => Assert.True(c is (>= '0' and <= '9') or (>= 'a' and <= 'f')));
    }

    [Fact]
    public void Hash_Does_Not_Contain_The_Input()
    {
        var token = Convert.ToBase64String(new byte[64]);

        Assert.DoesNotContain(token, _subject.Hash(token));
    }

    [Fact]
    public void Different_Inputs_Give_Different_Hashes()
    {
        Assert.NotEqual(_subject.Hash("token-a"), _subject.Hash("token-b"));
    }

    [Fact]
    public void Hash_Matches_A_Known_Sha256_Vector()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", _subject.Hash("abc"));
    }
}
