namespace Auth.Application.Models;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public int AccessTokenExpirationMinutes { get; set; }
    public int RefreshTokenExpirationDays { get; set; }
    public int RefreshTokenFamilyMaxDays { get; set; } = 30;

    public TimeSpan GetRefreshTokenLifetime() =>
        RefreshTokenExpirationDays > 0
            ? TimeSpan.FromDays(RefreshTokenExpirationDays)
            : throw new InvalidOperationException(
                $"{nameof(RefreshTokenExpirationDays)} must be greater than zero.");

    public TimeSpan GetRefreshTokenFamilyMaxLifetime() =>
        RefreshTokenFamilyMaxDays > 0
            ? TimeSpan.FromDays(RefreshTokenFamilyMaxDays)
            : throw new InvalidOperationException(
                $"{nameof(RefreshTokenFamilyMaxDays)} must be greater than zero.");

    // Checked at startup: a cap shorter than one sliding window would make the sliding lifetime meaningless.
    public static bool AreRefreshSettingsValid(JwtOptions options) =>
        options.RefreshTokenExpirationDays > 0
        && options.RefreshTokenFamilyMaxDays >= options.RefreshTokenExpirationDays;
}
