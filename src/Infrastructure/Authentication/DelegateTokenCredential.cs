using Azure.Core;
using M365MailMirror.Core.Authentication;

namespace M365MailMirror.Infrastructure.Authentication;

/// <summary>
/// A TokenCredential implementation that delegates token acquisition to an IAuthenticationService.
/// Caches tokens in memory to avoid excessive MSAL calls that can trigger AAD throttling.
/// </summary>
public class DelegateTokenCredential : TokenCredential
{
    private readonly IAuthenticationService _authService;
    private readonly object _lock = new();
    private AccessToken? _cachedToken;

    /// <summary>
    /// Time buffer before token expiry to trigger proactive refresh.
    /// Tokens are refreshed 5 minutes before actual expiry to avoid edge cases.
    /// </summary>
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Creates a new instance of DelegateTokenCredential.
    /// </summary>
    /// <param name="authService">The authentication service to use for token acquisition.</param>
    public DelegateTokenCredential(IAuthenticationService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    /// <inheritdoc />
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        // Use AsTask() to convert ValueTask to Task, which is safe to block on
        return GetTokenAsync(requestContext, cancellationToken).AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        // Fast path: return cached token if still valid (with buffer for proactive refresh)
        lock (_lock)
        {
            if (_cachedToken.HasValue &&
                _cachedToken.Value.ExpiresOn > DateTimeOffset.UtcNow.Add(RefreshBuffer))
            {
                return _cachedToken.Value;
            }
        }

        // Slow path: acquire new token from auth service
        var result = await _authService.AcquireTokenSilentAsync(cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to acquire token: {result.ErrorMessage}");
        }

        // Use a default expiration if not provided
        var expiresOn = result.ExpiresOn ?? DateTimeOffset.UtcNow.AddHours(1);
        var newToken = new AccessToken(result.AccessToken!, expiresOn);

        lock (_lock)
        {
            _cachedToken = newToken;
        }

        return newToken;
    }
}
