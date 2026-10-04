namespace SmartAppointments.Gateway.Configuration;

/// <summary>
/// Validates <c>Cors:AllowedOrigins</c> and returns the origins to allow. Blank entries mean "no origin
/// configured" (docker compose passes an empty one when WEB_ORIGIN is unset) and are dropped; duplicates
/// are collapsed. Any other malformed entry fails startup, because the browser compares the origin
/// ordinally and a non-canonical entry would silently never match.
/// </summary>
public static class CorsOriginsValidator
{
    public const string Key = "Cors:AllowedOrigins";

    public static IReadOnlyList<string> Validate(IConfiguration configuration)
    {
        var origins = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var child in configuration.GetSection(Key).GetChildren())
        {
            var entry = child.Value;
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var reason = Reject(entry);
            if (reason is not null)
            {
                throw new InvalidOperationException(
                    $"'{Key}:{child.Key}' must be an origin, scheme://host[:port] in lower case with no path, trailing slash, " +
                    $"query, fragment, user information or wildcard, but is '{entry}' ({reason}). Set it in " +
                    $"appsettings.Development.json or with the environment variable 'Cors__AllowedOrigins__{child.Key}'.");
            }

            if (seen.Add(entry))
            {
                origins.Add(entry);
            }
        }

        return origins;
    }

    private static string? Reject(string entry)
    {
        if (entry.Contains('*'))
        {
            return "wildcards are not allowed";
        }

        if (!Uri.TryCreate(entry, UriKind.Absolute, out var uri))
        {
            return "not an absolute URL";
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return "the scheme must be http or https";
        }

        if (uri.UserInfo.Length > 0)
        {
            return "user information is not allowed";
        }

        if (uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            return "a query or fragment is not allowed";
        }

        if (!string.Equals(entry, uri.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal))
        {
            return "not in the canonical form a browser sends: no path or trailing slash, lower-case scheme and host, no default port";
        }

        return null;
    }
}
