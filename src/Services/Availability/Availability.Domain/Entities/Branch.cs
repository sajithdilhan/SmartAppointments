namespace Availability.Domain.Entities;

public sealed class Branch
{
    private Branch() { }

    public Guid Id { get; private set; }

    /// <summary>
    /// The human-readable key, unique across active and inactive branches. Immutable once
    /// assigned, because Booking and Queue copy it into their own records and events.
    /// </summary>
    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public string? Description { get; private set; }

    public string Address { get; private set; } = default!;

    public string PhoneNumber { get; private set; } = default!;

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public static Branch Create(
        string code,
        string name,
        string? description,
        string address,
        string phoneNumber)
    {
        return new Branch
        {
            Id = Guid.CreateVersion7(),
            Code = NormaliseCode(code),
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Address = address.Trim(),
            PhoneNumber = phoneNumber.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Replaces the editable details with the same trimming as <see cref="Create"/>. The code is
    /// deliberately not a parameter, and <see cref="IsActive"/> is left alone: activation has its
    /// own operations.
    /// </summary>
    public void UpdateDetails(string name, string? description, string address, string phoneNumber)
    {
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Address = address.Trim();
        PhoneNumber = phoneNumber.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Returns false, and changes nothing, when the branch is already active, so the caller can
    /// skip the write.
    /// </summary>
    public bool Activate() => SetActive(true);

    /// <summary>
    /// Returns false, and changes nothing, when the branch is already inactive, so the caller can
    /// skip the write.
    /// </summary>
    public bool Deactivate() => SetActive(false);

    private bool SetActive(bool isActive)
    {
        if (IsActive == isActive)
        {
            return false;
        }

        IsActive = isActive;
        UpdatedAtUtc = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// The one normalisation rule for codes. The duplicate check runs its input through this
    /// too, so the value looked up and the value stored cannot drift apart.
    /// </summary>
    public static string NormaliseCode(string code) => code.Trim().ToUpperInvariant();
}
