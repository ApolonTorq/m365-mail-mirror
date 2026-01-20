namespace M365MailMirror.Core.Authentication;

/// <summary>
/// Service for handling Microsoft 365 authentication via device code flow.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Initiates device code flow authentication.
    /// </summary>
    /// <param name="deviceCodeCallback">Callback to display device code information to the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authentication result.</returns>
    Task<AppAuthenticationResult> AuthenticateWithDeviceCodeAsync(
        Action<DeviceCodeInfo> deviceCodeCallback,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to acquire a token silently using cached credentials.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authentication result, or failure if no cached credentials exist.</returns>
    Task<AppAuthenticationResult> AcquireTokenSilentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current authentication status.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authentication status.</returns>
    Task<AuthenticationStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs out and clears all cached tokens.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SignOutAsync(CancellationToken cancellationToken = default);
}
