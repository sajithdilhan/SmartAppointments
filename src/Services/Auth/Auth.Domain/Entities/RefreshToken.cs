namespace Auth.Domain.Entities;

/// <summary>
/// One refresh token of a family: the chain that starts at a login and grows by one row per refresh.
/// Only the hash of the secret is kept. Revocation is not a method here: it is a conditional
/// statement in the repository, because a tracked entity changed in memory would reintroduce the
/// read-then-write race that rotation must avoid.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>The login that began the family; unchanged by rotation.</summary>
    public Guid FamilyId { get; private set; }

    public string TokenHash { get; private set; } = default!;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>The login time; copied unchanged into every successor.</summary>
    public DateTime FamilyStartedAtUtc { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    /// <summary>The successor, set only by rotation; null for a token revoked by logout or reuse.</summary>
    public Guid? ReplacedById { get; private set; }

    public bool IsRevoked => RevokedAtUtc is not null;

    public bool IsExpired(DateTime nowUtc) => ExpiresAtUtc <= nowUtc;

    public static RefreshToken StartFamily(
        Guid userId,
        string tokenHash,
        DateTime nowUtc,
        TimeSpan lifetime,
        TimeSpan familyMaxLifetime)
    {
        return Create(userId, Guid.CreateVersion7(), tokenHash, nowUtc, nowUtc, lifetime, familyMaxLifetime);
    }

    /// <summary>
    /// The token that replaces this one, in the same family. Returns null when the family is already
    /// at its absolute cap, so a session cannot outlive the cap without a new login.
    /// </summary>
    public RefreshToken? CreateSuccessor(
        string tokenHash,
        DateTime nowUtc,
        TimeSpan lifetime,
        TimeSpan familyMaxLifetime)
    {
        var successor = Create(UserId, FamilyId, tokenHash, nowUtc, FamilyStartedAtUtc, lifetime, familyMaxLifetime);
        return successor.ExpiresAtUtc <= nowUtc ? null : successor;
    }

    // Sliding window under an absolute cap: the earlier of "now + lifetime" and "family start + cap".
    private static RefreshToken Create(
        Guid userId,
        Guid familyId,
        string tokenHash,
        DateTime nowUtc,
        DateTime familyStartedAtUtc,
        TimeSpan lifetime,
        TimeSpan familyMaxLifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(familyMaxLifetime, TimeSpan.Zero);

        var sliding = nowUtc + lifetime;
        var cap = familyStartedAtUtc + familyMaxLifetime;

        return new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = tokenHash,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = sliding < cap ? sliding : cap,
            FamilyStartedAtUtc = familyStartedAtUtc
        };
    }
}
