namespace M365MailMirror.Core.Authentication;

/// <summary>
/// Represents the current authentication status.
/// </summary>
public class AuthenticationStatus
{
    /// <summary>
    /// Gets whether the user is authenticated.
    /// </summary>
    public bool IsAuthenticated { get; init; }

    /// <summary>
    /// Gets the authenticated account email, if any.
    /// </summary>
    public string? Account { get; init; }

    /// <summary>
    /// Gets the tenant ID, if known.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Gets whether a valid cached token exists.
    /// </summary>
    public bool HasCachedToken { get; init; }

    /// <summary>
    /// Gets the cache location description (for status display).
    /// </summary>
    public string? CacheLocation { get; init; }
}
