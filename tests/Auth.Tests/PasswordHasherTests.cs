using Auth.Infrastructure.Services;

namespace Auth.Tests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _subject = new();

    [Fact]
    public void Hash_Then_Verify_Round_Trips()
    {
        var hash = _subject.Hash("P@ssw0rd!23");

        Assert.True(_subject.Verify("P@ssw0rd!23", hash));
    }

    [Fact]
    public void Verify_Rejects_A_Wrong_Password()
    {
        var hash = _subject.Hash("P@ssw0rd!23");

        Assert.False(_subject.Verify("P@ssw0rd!24", hash));
    }

    [Fact]
    public void Verify_Is_Case_Sensitive()
    {
        var hash = _subject.Hash("P@ssw0rd!23");

        Assert.False(_subject.Verify("p@ssw0rd!23", hash));
    }

    [Fact]
    public void Hash_Does_Not_Store_The_Plaintext()
    {
        var hash = _subject.Hash("P@ssw0rd!23");

        Assert.DoesNotContain("P@ssw0rd!23", hash);
    }

    [Fact]
    public void The_Same_Password_Hashes_Differently_Each_Time()
    {
        // BCrypt salts per call, so identical passwords must not produce identical hashes —
        // otherwise the stored column would reveal which accounts share a password.
        Assert.NotEqual(_subject.Hash("P@ssw0rd!23"), _subject.Hash("P@ssw0rd!23"));
    }
}
