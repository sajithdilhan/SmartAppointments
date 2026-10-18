using Auth.Domain.ValueObjects;
using SmartAppointments.BuildingBlocks.Enums;

namespace Auth.Domain.Entities;

public sealed class User
{
    private User()
    {
    }

    public Guid Id { get; private set; }

    public string FirstName { get; private set; } = default!;

    public string LastName { get; private set; } = default!;

    public Email Email { get; private set; } = default!;

    public string PhoneNumber { get; private set; } = default!;

    public string PasswordHash { get; private set; } = default!;

    public UserRole Role { get; private set; }

    public DateTime RegistrationDateUtc { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime? LastLoginAtUtc { get; private set; }

    /// <summary>The branch a Staff user works at; null for every other role.</summary>
    public Guid? BranchId { get; private set; }

    public static User RegisterCustomer(
        string firstName,
        string lastName,
        Email email,
        string phoneNumber,
        string passwordHash)
    {
        return new User
        {
            Id = Guid.CreateVersion7(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Email = email,
            PhoneNumber = phoneNumber.Trim(),
            PasswordHash = passwordHash,
            Role = UserRole.Customer,
            RegistrationDateUtc = DateTime.UtcNow,
            IsActive = true
        };
    }

    public static User RegisterStaff(
        string firstName,
        string lastName,
        Email email,
        string phoneNumber,
        string passwordHash,
        Guid branchId)
    {
        if (branchId == Guid.Empty)
        {
            throw new ArgumentException("A staff member requires a branch.", nameof(branchId));
        }

        return new User
        {
            Id = Guid.CreateVersion7(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Email = email,
            PhoneNumber = phoneNumber.Trim(),
            PasswordHash = passwordHash,
            Role = UserRole.Staff,
            RegistrationDateUtc = DateTime.UtcNow,
            IsActive = true,
            BranchId = branchId
        };
    }

    public static User RegisterAdmin(
        string firstName,
        string lastName,
        Email email,
        string phoneNumber,
        string passwordHash)
    {
        return new User
        {
            Id = Guid.CreateVersion7(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Email = email,
            PhoneNumber = phoneNumber.Trim(),
            PasswordHash = passwordHash,
            Role = UserRole.Admin,
            RegistrationDateUtc = DateTime.UtcNow,
            IsActive = true
        };
    }

    public string FullName => $"{FirstName} {LastName}";

    public void RecordLogin()
    {
        LastLoginAtUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void AssignBranch(Guid branchId)
    {
        if (Role != UserRole.Staff)
        {
            throw new InvalidOperationException("Only a staff user can be assigned to a branch.");
        }

        if (branchId == Guid.Empty)
        {
            throw new ArgumentException("A branch is required.", nameof(branchId));
        }

        BranchId = branchId;
    }

    public void ChangePassword(string newPasswordHash)
    {
        PasswordHash = newPasswordHash;
    }
}
