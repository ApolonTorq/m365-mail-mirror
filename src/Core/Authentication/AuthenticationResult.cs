namespace M365MailMirror.Core.Authentication;

/// <summary>
/// Represents the result of an authentication attempt.
/// </summary>
public class AppAuthenticationResult
{
    /// <summary>
    /// Gets whether the authentication was successful.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Gets the access token if authentication was successful.
    /// </summary>
    public string? AccessToken { get; init; }

    /// <summary>
    /// Gets the authenticated user's email address.
    /// </summary>
    public string? Account { get; init; }

    /// <summary>
    /// Gets the token expiration time.
    /// </summary>
    public DateTimeOffset? ExpiresOn { get; init; }

    /// <summary>
    /// Gets the error message if authentication failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful authentication result.
    /// </summary>
    public static AppAuthenticationResult Success(string accessToken, string account, DateTimeOffset expiresOn)
        => new()
        {
            IsSuccess = true,
            AccessToken = accessToken,
            Account = account,
            ExpiresOn = expiresOn
        };

    /// <summary>
    /// Creates a failed authentication result.
    /// </summary>
    public static AppAuthenticationResult Failure(string errorMessage)
        => new()
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
}
