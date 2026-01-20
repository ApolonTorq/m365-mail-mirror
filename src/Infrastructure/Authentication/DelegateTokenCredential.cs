using Azure.Core;
using M365MailMirror.Core.Authentication;

namespace M365MailMirror.Infrastructure.Authentication;

/// <summary>
/// A TokenCredential implementation that delegates token acquisition to an IAuthenticationService.
/// </summary>
public class DelegateTokenCredential : TokenCredential
{
    private readonly IAuthenticationService _authService;

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
        var result = await _authService.AcquireTokenSilentAsync(cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to acquire token: {result.ErrorMessage}");
        }

        // Use a default expiration if not provided
        var expiresOn = result.ExpiresOn ?? DateTimeOffset.UtcNow.AddHours(1);
        return new AccessToken(result.AccessToken!, expiresOn);
    }
}
