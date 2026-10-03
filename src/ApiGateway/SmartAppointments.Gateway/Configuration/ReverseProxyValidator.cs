namespace SmartAppointments.Gateway.Configuration;

/// <summary>
/// Fails startup, with a message naming the missing key, when a downstream cluster has no usable
/// destination address. The addresses are blank in appsettings.json and come from
/// appsettings.Development.json or the environment, so an unconfigured environment must not run
/// half-configured and silently proxy nowhere.
/// </summary>
public static class ReverseProxyValidator
{
    public static readonly string[] Clusters = ["auth", "availability", "booking"];

    public static void Validate(IConfiguration configuration)
    {
        foreach (var cluster in Clusters)
        {
            var destinations = configuration.GetSection($"ReverseProxy:Clusters:{cluster}:Destinations").GetChildren().ToList();
            if (destinations.Count == 0)
            {
                throw new InvalidOperationException(
                    $"'ReverseProxy:Clusters:{cluster}:Destinations' has no destination. Configure at least one, " +
                    $"for example with the environment variable 'ReverseProxy__Clusters__{cluster}__Destinations__primary__Address'.");
            }

            foreach (var destination in destinations)
            {
                var key = $"ReverseProxy:Clusters:{cluster}:Destinations:{destination.Key}:Address";
                var address = destination["Address"];

                if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    throw new InvalidOperationException(
                        $"'{key}' must be an absolute http or https URL but is '{address}'. Set it in " +
                        $"appsettings.Development.json or with the environment variable '{key.Replace(":", "__")}'.");
                }
            }
        }
    }
}
