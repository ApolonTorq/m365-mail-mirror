namespace M365MailMirror.Core.Logging;

/// <summary>
/// Application logger interface for structured logging.
/// </summary>
public interface IAppLogger
{
    /// <summary>
    /// Gets or sets the minimum log level to output.
    /// </summary>
    AppLogLevel MinimumLevel { get; set; }

    /// <summary>
    /// Logs a debug message.
    /// </summary>
    void Debug(string message, params object[] args);

    /// <summary>
    /// Logs an informational message.
    /// </summary>
    void Info(string message, params object[] args);

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    void Warning(string message, params object[] args);

    /// <summary>
    /// Logs an error message.
    /// </summary>
    void Error(string message, params object[] args);

    /// <summary>
    /// Logs an error message with an exception.
    /// </summary>
    void Error(Exception exception, string message, params object[] args);

    /// <summary>
    /// Creates a scoped logger with a specific context name.
    /// </summary>
    IAppLogger ForContext(string contextName);
}
