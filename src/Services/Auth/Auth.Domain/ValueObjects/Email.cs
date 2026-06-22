namespace Auth.Domain.ValueObjects;

public sealed record Email
{
    public string Value { get; }

    private Email(string value)
    {
        Value = value;
    }

    public static Email Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Email is required.", nameof(value));

        var normalizedEmail = value.Trim().ToLowerInvariant();

        if (!normalizedEmail.Contains('@'))
            throw new ArgumentException("Email is invalid.", nameof(value));

        return new Email(normalizedEmail);
    }

    public override string ToString() => Value;
}