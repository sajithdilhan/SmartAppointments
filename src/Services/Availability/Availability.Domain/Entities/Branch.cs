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

    /// <summary>
    /// The IANA zone the working hours are expressed in. Null until a schedule is set, and slots
    /// cannot be generated before then.
    /// </summary>
    public string? TimeZoneId { get; private set; }

    private readonly List<WorkingHours> _workingHours = [];

    /// <summary>
    /// The open days, in no particular order. Use <see cref="WorkingHours.MondayFirstOrder"/> to
    /// present them Monday first.
    /// </summary>
    public IReadOnlyList<WorkingHours> WorkingHours => _workingHours;

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
    /// Replaces the weekly schedule. The collection is updated in place: retained days get their
    /// new times, unlisted days are removed and new days added. A day is never removed and
    /// re-added, which EF Core would reject because the day is part of the key.
    /// </summary>
    public void SetSchedule(string timeZoneId, IEnumerable<WorkingHours> workingHours)
    {
        var incoming = workingHours.ToList();
        if (incoming.Select(w => w.DayOfWeek).Distinct().Count() != incoming.Count)
        {
            throw new ArgumentException("A day of the week can be listed only once.", nameof(workingHours));
        }

        _workingHours.RemoveAll(existing => incoming.All(w => w.DayOfWeek != existing.DayOfWeek));

        foreach (var hours in incoming)
        {
            var existing = _workingHours.FirstOrDefault(w => w.DayOfWeek == hours.DayOfWeek);
            if (existing is null)
            {
                _workingHours.Add(new WorkingHours(hours.DayOfWeek, hours.OpensAt, hours.ClosesAt));
            }
            else
            {
                existing.ChangeTimes(hours.OpensAt, hours.ClosesAt);
            }
        }

        TimeZoneId = timeZoneId;
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
