namespace M365MailMirror.Core.Exceptions;

/// <summary>
/// Standard exit codes for the CLI application.
/// Exit codes 1-255 are valid on all platforms (Unix limits to 8-bit unsigned).
/// </summary>
public static class CliExitCodes
{
    /// <summary>Command completed successfully.</summary>
    public const int Success = 0;

    /// <summary>Unspecified or general error.</summary>
    public const int GeneralError = 1;

    /// <summary>Configuration file parsing or validation error.</summary>
    public const int ConfigurationError = 2;

    /// <summary>Authentication failed, token expired, or no credentials.</summary>
    public const int AuthenticationError = 3;

    /// <summary>Network error such as API timeout or connection failure.</summary>
    public const int NetworkError = 4;

    /// <summary>File system error such as permission denied or disk full.</summary>
    public const int FileSystemError = 5;

    /// <summary>Database error such as corruption or migration failure.</summary>
    public const int DatabaseError = 6;

    /// <summary>
    /// Operation cancelled by user (Ctrl+C).
    /// Unix convention: 128 + SIGINT (2) = 130.
    /// </summary>
    public const int Cancelled = 130;
}
