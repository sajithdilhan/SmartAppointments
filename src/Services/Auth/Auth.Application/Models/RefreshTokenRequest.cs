namespace Auth.Application.Models;

// string? on purpose: a non-nullable string would make MVC's implicit [Required] reject a missing
// value with a body that is not ours. The validator produces the "Invalid request: ..." message instead.
public sealed record RefreshTokenRequest(string? RefreshToken);
