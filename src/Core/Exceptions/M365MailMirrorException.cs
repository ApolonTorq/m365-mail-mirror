namespace M365MailMirror.Core.Exceptions;

/// <summary>
/// Base exception for m365-mail-mirror with exit code support.
/// Derived exceptions can specify appropriate exit codes for different error categories.
/// </summary>
public class M365MailMirrorException : Exception
{
    /// <summary>
    /// Gets the exit code associated with this exception.
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    /// Initializes a new instance with the specified message and default exit code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="exitCode">The exit code (default: GeneralError).</param>
    public M365MailMirrorException(string message, int exitCode = CliExitCodes.GeneralError)
        : base(message)
    {
        ExitCode = exitCode;
    }

    /// <summary>
    /// Initializes a new instance with the specified message, inner exception, and exit code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception that caused this exception.</param>
    /// <param name="exitCode">The exit code (default: GeneralError).</param>
    public M365MailMirrorException(string message, Exception innerException, int exitCode = CliExitCodes.GeneralError)
        : base(message, innerException)
    {
        ExitCode = exitCode;
    }
}
