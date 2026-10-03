namespace Availability.Domain.Entities;

/// <summary>
/// Something a customer can book or queue for, with a fixed duration. One catalogue is shared by
/// every branch; a branch offers a service when slots exist for that pair.
/// </summary>
public sealed class ServiceType
{
    private ServiceType() { }

    public Guid Id { get; private set; }

    /// <summary>
    /// The human-readable key, unique across active and inactive service types. Immutable once
    /// assigned, because Booking and Queue copy it into their own records and events.
    /// </summary>
    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public string? Description { get; private set; }

    /// <summary>
    /// The length of each slot generated for this service. Changing it leaves existing slots alone.
    /// </summary>
    public int DurationMinutes { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public static ServiceType Create(string code, string name, string? description, int durationMinutes)
    {
        return new ServiceType
        {
            Id = Guid.CreateVersion7(),
            Code = NormaliseCode(code),
            Name = name.Trim(),
            Description = NormaliseDescription(description),
            DurationMinutes = durationMinutes,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Replaces the editable details. The code is deliberately not a parameter, and
    /// <see cref="IsActive"/> is left alone: activation has its own operations.
    /// </summary>
    public void UpdateDetails(string name, string? description, int durationMinutes)
    {
        Name = name.Trim();
        Description = NormaliseDescription(description);
        DurationMinutes = durationMinutes;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Returns false, and changes nothing, when the service type is already active.
    /// </summary>
    public bool Activate() => SetActive(true);

    /// <summary>
    /// Returns false, and changes nothing, when the service type is already inactive.
    /// </summary>
    public bool Deactivate() => SetActive(false);

    /// <summary>
    /// The one normalisation rule for codes, shared with the handler's duplicate check.
    /// </summary>
    public static string NormaliseCode(string code) => code.Trim().ToUpperInvariant();

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

    private static string? NormaliseDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
