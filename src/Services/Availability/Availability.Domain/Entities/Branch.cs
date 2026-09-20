namespace Availability.Domain.Entities;

public sealed class Branch
{
    private Branch() { }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = default!;

    public string Description { get; private set; } = default!;

    public string Address { get; private set; } = default!;

    public string PhoneNumber { get; private set; } = default!;

    public static Branch Create(
        string name,
        string description,
        string address,
        string phoneNumber)
    {
        return new Branch
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Description = description.Trim(),
            Address = address.Trim(),
            PhoneNumber = phoneNumber.Trim()
        };
    }
}
