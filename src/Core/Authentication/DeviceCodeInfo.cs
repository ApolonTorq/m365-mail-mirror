namespace M365MailMirror.Core.Authentication;

/// <summary>
/// Contains the device code information for user display.
/// </summary>
public class DeviceCodeInfo
{
    /// <summary>
    /// Gets the user code to enter at the verification URL.
    /// </summary>
    public required string UserCode { get; init; }

    /// <summary>
    /// Gets the verification URL where the user enters the code.
    /// </summary>
    public required string VerificationUrl { get; init; }

    /// <summary>
    /// Gets the complete user message for display.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the device code expiration time.
    /// </summary>
    public DateTimeOffset ExpiresOn { get; init; }
}
