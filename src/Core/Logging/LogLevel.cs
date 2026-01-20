namespace M365MailMirror.Core.Logging;

/// <summary>
/// Log severity levels for the application.
/// </summary>
public enum AppLogLevel
{
    /// <summary>
    /// Debug-level messages for development troubleshooting.
    /// </summary>
    Debug = 0,

    /// <summary>
    /// Informational messages about normal operation.
    /// </summary>
    Info = 1,

    /// <summary>
    /// Warning messages about potential issues.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// Error messages about failures that don't stop execution.
    /// </summary>
    Error = 3,

    /// <summary>
    /// No logging.
    /// </summary>
    None = 4
}
