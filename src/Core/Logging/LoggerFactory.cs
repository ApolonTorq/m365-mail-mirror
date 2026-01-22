namespace M365MailMirror.Core.Logging;

/// <summary>
/// Factory for creating application loggers.
/// Supports configurable output destinations for test isolation.
/// </summary>
public static class LoggerFactory
{
    private static IAppLogger? _defaultLogger;
    private static AppLogLevel _defaultLevel = AppLogLevel.Info;
    private static TextWriter? _outputWriter;
    private static TextWriter? _errorWriter;

    /// <summary>
    /// Gets or sets the default minimum log level.
    /// </summary>
    public static AppLogLevel DefaultLevel
    {
        get => _defaultLevel;
        set
        {
            _defaultLevel = value;
            if (_defaultLogger != null)
            {
                _defaultLogger.MinimumLevel = value;
            }
        }
    }

    /// <summary>
    /// Gets the default logger instance.
    /// Uses configured output writers if set, otherwise defaults to Console.Out/Error.
    /// </summary>
    public static IAppLogger Default => _defaultLogger ??= new ConsoleLogger(
        _defaultLevel,
        null,
        _outputWriter ?? Console.Out,
        _errorWriter ?? Console.Error);

    /// <summary>
    /// Creates a new console logger with the specified minimum level.
    /// </summary>
    /// <param name="minimumLevel">The minimum log level to output.</param>
    /// <returns>A new logger instance.</returns>
    public static IAppLogger CreateConsoleLogger(AppLogLevel minimumLevel = AppLogLevel.Info)
    {
        return new ConsoleLogger(minimumLevel);
    }

    /// <summary>
    /// Creates a new logger for a specific context.
    /// </summary>
    /// <param name="contextName">The context name (typically a class or component name).</param>
    /// <returns>A scoped logger instance.</returns>
    public static IAppLogger CreateLogger(string contextName)
    {
        return Default.ForContext(contextName);
    }

    /// <summary>
    /// Creates a new logger for the specified type.
    /// </summary>
    /// <typeparam name="T">The type to create a logger for.</typeparam>
    /// <returns>A scoped logger instance.</returns>
    public static IAppLogger CreateLogger<T>()
    {
        return CreateLogger(typeof(T).Name);
    }

    /// <summary>
    /// Configures the default logger with the specified settings.
    /// </summary>
    /// <param name="minimumLevel">The minimum log level to output.</param>
    /// <param name="verbose">If true, sets minimum level to Debug.</param>
    /// <param name="output">Custom output writer for standard log messages. Defaults to Console.Out.</param>
    /// <param name="error">Custom output writer for error messages. Defaults to Console.Error.</param>
    public static void Configure(
        AppLogLevel? minimumLevel = null,
        bool verbose = false,
        TextWriter? output = null,
        TextWriter? error = null)
    {
        if (verbose)
        {
            DefaultLevel = AppLogLevel.Debug;
        }
        else if (minimumLevel.HasValue)
        {
            DefaultLevel = minimumLevel.Value;
        }

        _outputWriter = output;
        _errorWriter = error;

        // Reset default logger to pick up new settings
        _defaultLogger = null;
    }

    /// <summary>
    /// Resets the logger factory to default settings.
    /// Clears any configured output writers, reverting to Console.Out/Error.
    /// </summary>
    public static void Reset()
    {
        _defaultLogger = null;
        _defaultLevel = AppLogLevel.Info;
        _outputWriter = null;
        _errorWriter = null;
    }
}
