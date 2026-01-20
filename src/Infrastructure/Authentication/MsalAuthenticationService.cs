using Microsoft.Identity.Client;
using M365MailMirror.Core.Authentication;
using M365MailMirror.Core.Logging;

namespace M365MailMirror.Infrastructure.Authentication;

/// <summary>
/// Microsoft Authentication Library (MSAL) based authentication service
/// implementing device code flow for Microsoft 365.
/// </summary>
public class MsalAuthenticationService : IAuthenticationService
{
    private readonly IPublicClientApplication _msalClient;
    private readonly ITokenCacheStorage _tokenCacheStorage;
    private readonly IAppLogger _logger;
    private readonly string[] _scopes;

    /// <summary>
    /// Default Microsoft Graph scopes required for mail access.
    /// </summary>
    public static readonly string[] DefaultScopes =
    [
        "https://graph.microsoft.com/Mail.ReadWrite",
        "https://graph.microsoft.com/offline_access"
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="MsalAuthenticationService"/> class.
    /// </summary>
    /// <param name="clientId">The Azure AD application (client) ID.</param>
    /// <param name="tenantId">The Azure AD tenant ID (or "common" for multi-tenant).</param>
    /// <param name="tokenCacheStorage">Token cache storage implementation.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="scopes">OAuth scopes to request. Defaults to Mail.ReadWrite and offline_access.</param>
    public MsalAuthenticationService(
        string clientId,
        string tenantId,
        ITokenCacheStorage tokenCacheStorage,
        IAppLogger? logger = null,
        string[]? scopes = null)
    {
        _tokenCacheStorage = tokenCacheStorage;
        _logger = logger ?? LoggerFactory.CreateLogger<MsalAuthenticationService>();
        _scopes = scopes ?? DefaultScopes;

        var authority = $"https://login.microsoftonline.com/{tenantId}";

        _msalClient = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority(authority)
            .WithDefaultRedirectUri()
            .Build();

        // Set up token cache serialization
        _msalClient.UserTokenCache.SetBeforeAccessAsync(OnBeforeAccessAsync);
        _msalClient.UserTokenCache.SetAfterAccessAsync(OnAfterAccessAsync);
    }

    /// <inheritdoc />
    public async Task<AppAuthenticationResult> AuthenticateWithDeviceCodeAsync(
        Action<DeviceCodeInfo> deviceCodeCallback,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.Debug("Starting device code flow authentication");

            var result = await _msalClient.AcquireTokenWithDeviceCode(_scopes, deviceCodeResult =>
            {
                var deviceCodeInfo = new DeviceCodeInfo
                {
                    UserCode = deviceCodeResult.UserCode,
                    VerificationUrl = deviceCodeResult.VerificationUrl?.ToString() ?? "https://microsoft.com/devicelogin",
                    Message = deviceCodeResult.Message,
                    ExpiresOn = deviceCodeResult.ExpiresOn
                };

                deviceCodeCallback(deviceCodeInfo);
                return Task.CompletedTask;
            }).ExecuteAsync(cancellationToken);

            _logger.Info("Successfully authenticated as {0}", result.Account.Username);

            return AppAuthenticationResult.Success(
                result.AccessToken,
                result.Account.Username,
                result.ExpiresOn);
        }
        catch (MsalServiceException ex) when (ex.ErrorCode == "authorization_pending")
        {
            _logger.Debug("Authorization pending - user has not completed authentication");
            return AppAuthenticationResult.Failure("Authentication was not completed. Please try again.");
        }
        catch (MsalServiceException ex) when (ex.ErrorCode == "code_expired")
        {
            _logger.Warning("Device code expired");
            return AppAuthenticationResult.Failure("The device code has expired. Please try again.");
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Authentication cancelled by user");
            return AppAuthenticationResult.Failure("Authentication was cancelled.");
        }
        catch (MsalException ex)
        {
            _logger.Error(ex, "MSAL authentication error");
            return AppAuthenticationResult.Failure($"Authentication failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected authentication error");
            return AppAuthenticationResult.Failure($"An unexpected error occurred: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<AppAuthenticationResult> AcquireTokenSilentAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.Debug("Attempting to acquire token silently");

            var accounts = await _msalClient.GetAccountsAsync();
            var account = accounts.FirstOrDefault();

            if (account == null)
            {
                _logger.Debug("No cached account found");
                return AppAuthenticationResult.Failure("No cached credentials found. Please run 'auth login' first.");
            }

            var result = await _msalClient.AcquireTokenSilent(_scopes, account)
                .ExecuteAsync(cancellationToken);

            _logger.Debug("Successfully acquired token silently for {0}", account.Username);

            return AppAuthenticationResult.Success(
                result.AccessToken,
                result.Account.Username,
                result.ExpiresOn);
        }
        catch (MsalUiRequiredException ex)
        {
            _logger.Warning("Silent token acquisition failed - user interaction required: {0}", ex.Message);
            return AppAuthenticationResult.Failure("Cached credentials have expired. Please run 'auth login' to re-authenticate.");
        }
        catch (MsalException ex)
        {
            _logger.Error(ex, "MSAL error during silent token acquisition");
            return AppAuthenticationResult.Failure($"Failed to refresh token: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected error during silent token acquisition");
            return AppAuthenticationResult.Failure($"An unexpected error occurred: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<AuthenticationStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var accounts = await _msalClient.GetAccountsAsync();
            var account = accounts.FirstOrDefault();
            var hasCache = await _tokenCacheStorage.ExistsAsync();

            if (account == null)
            {
                return new AuthenticationStatus
                {
                    IsAuthenticated = false,
                    HasCachedToken = hasCache,
                    CacheLocation = _tokenCacheStorage.StorageDescription
                };
            }

            // Try to acquire a token silently to verify the cache is valid
            try
            {
                var result = await _msalClient.AcquireTokenSilent(_scopes, account)
                    .ExecuteAsync(cancellationToken);

                return new AuthenticationStatus
                {
                    IsAuthenticated = true,
                    Account = account.Username,
                    TenantId = account.HomeAccountId?.TenantId,
                    HasCachedToken = true,
                    CacheLocation = _tokenCacheStorage.StorageDescription
                };
            }
            catch (MsalUiRequiredException)
            {
                // Token exists but has expired
                return new AuthenticationStatus
                {
                    IsAuthenticated = false,
                    Account = account.Username,
                    TenantId = account.HomeAccountId?.TenantId,
                    HasCachedToken = true,
                    CacheLocation = _tokenCacheStorage.StorageDescription
                };
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error getting authentication status");
            return new AuthenticationStatus
            {
                IsAuthenticated = false,
                HasCachedToken = false,
                CacheLocation = _tokenCacheStorage.StorageDescription
            };
        }
    }

    /// <inheritdoc />
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.Debug("Signing out and clearing cached tokens");

            var accounts = await _msalClient.GetAccountsAsync();
            foreach (var account in accounts)
            {
                await _msalClient.RemoveAsync(account);
                _logger.Debug("Removed account: {0}", account.Username);
            }

            await _tokenCacheStorage.ClearAsync();
            _logger.Info("Successfully signed out");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during sign out");
            throw;
        }
    }

    private async Task OnBeforeAccessAsync(TokenCacheNotificationArgs args)
    {
        try
        {
            var data = await _tokenCacheStorage.ReadAsync();
            if (data != null)
            {
                args.TokenCache.DeserializeMsalV3(data);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("Failed to read token cache: {0}", ex.Message);
        }
    }

    private async Task OnAfterAccessAsync(TokenCacheNotificationArgs args)
    {
        if (args.HasStateChanged)
        {
            try
            {
                var data = args.TokenCache.SerializeMsalV3();
                await _tokenCacheStorage.WriteAsync(data);
            }
            catch (Exception ex)
            {
                _logger.Warning("Failed to write token cache: {0}", ex.Message);
            }
        }
    }
}
