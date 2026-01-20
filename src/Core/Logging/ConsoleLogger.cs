using System.Globalization;

namespace M365MailMirror.Core.Logging;

/// <summary>
/// Console-based logger with colored output and structured logging support.
/// </summary>
public class ConsoleLogger : IAppLogger
{
    private readonly string? _context;
    private readonly TextWriter _output;
    private readonly TextWriter _errorOutput;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleLogger"/> class.
    /// </summary>
    /// <param name="minimumLevel">The minimum log level to output.</param>
    public ConsoleLogger(AppLogLevel minimumLevel = AppLogLevel.Info)
        : this(minimumLevel, null, Console.Out, Console.Error)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleLogger"/> class with custom output streams.
    /// </summary>
    /// <param name="minimumLevel">The minimum log level to output.</param>
    /// <param name="context">The context name for scoped logging.</param>
    /// <param name="output">The standard output stream.</param>
    /// <param name="errorOutput">The error output stream.</param>
    public ConsoleLogger(AppLogLevel minimumLevel, string? context, TextWriter output, TextWriter errorOutput)
    {
        MinimumLevel = minimumLevel;
        _context = context;
        _output = output;
        _errorOutput = errorOutput;
    }

    /// <inheritdoc />
    public AppLogLevel MinimumLevel { get; set; }

    /// <inheritdoc />
    public void Debug(string message, params object[] args)
    {
        Log(AppLogLevel.Debug, message, args);
    }

    /// <inheritdoc />
    public void Info(string message, params object[] args)
    {
        Log(AppLogLevel.Info, message, args);
    }

    /// <inheritdoc />
    public void Warning(string message, params object[] args)
    {
        Log(AppLogLevel.Warning, message, args);
    }

    /// <inheritdoc />
    public void Error(string message, params object[] args)
    {
        Log(AppLogLevel.Error, message, args);
    }

    /// <inheritdoc />
    public void Error(Exception exception, string message, params object[] args)
    {
        Log(AppLogLevel.Error, message, args, exception);
    }

    /// <inheritdoc />
    public IAppLogger ForContext(string contextName)
    {
        var newContext = string.IsNullOrEmpty(_context)
            ? contextName
            : $"{_context}.{contextName}";

        return new ConsoleLogger(MinimumLevel, newContext, _output, _errorOutput);
    }

    private void Log(AppLogLevel level, string message, object[] args, Exception? exception = null)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        var formattedMessage = args.Length > 0 ? FormatMessage(message, args) : message;
        var timestamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var levelTag = GetLevelTag(level);
        var contextPrefix = string.IsNullOrEmpty(_context) ? "" : $"[{_context}] ";

        var output = level >= AppLogLevel.Error ? _errorOutput : _output;

        lock (_lock)
        {
            var originalColor = Console.ForegroundColor;

            try
            {
                // Write timestamp
                Console.ForegroundColor = ConsoleColor.DarkGray;
                output.Write($"{timestamp} ");

                // Write level tag with color
                Console.ForegroundColor = GetLevelColor(level);
                output.Write($"{levelTag} ");

                // Write context if present
                if (!string.IsNullOrEmpty(_context))
                {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    output.Write($"[{_context}] ");
                }

                // Write message
                Console.ForegroundColor = level == AppLogLevel.Error ? ConsoleColor.Red : originalColor;
                output.WriteLine(formattedMessage);

                // Write exception if present
                if (exception != null)
                {
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    output.WriteLine($"  Exception: {exception.GetType().Name}: {exception.Message}");

                    if (MinimumLevel == AppLogLevel.Debug && exception.StackTrace != null)
                    {
                        output.WriteLine($"  Stack trace:");
                        foreach (var line in exception.StackTrace.Split('\n'))
                        {
                            output.WriteLine($"    {line.Trim()}");
                        }
                    }
                }
            }
            finally
            {
                Console.ForegroundColor = originalColor;
            }
        }
    }

    private static string FormatMessage(string message, object[] args)
    {
        try
        {
            // Support both positional ({0}) and named ({name}) placeholders
            // For named placeholders, we just use positional replacement
            var formattedMessage = message;
            for (var i = 0; i < args.Length; i++)
            {
                // Replace positional placeholders
                formattedMessage = formattedMessage.Replace($"{{{i}}}", args[i]?.ToString() ?? "null");
            }

            // If there are still curly braces, try standard string format
            if (formattedMessage.Contains('{') && formattedMessage.Contains('}'))
            {
                try
                {
                    formattedMessage = string.Format(CultureInfo.InvariantCulture, message, args);
                }
                catch
                {
                    // Keep the partially formatted message
                }
            }

            return formattedMessage;
        }
        catch
        {
            // Fallback to original message if formatting fails
            return message;
        }
    }

    private static string GetLevelTag(AppLogLevel level) => level switch
    {
        AppLogLevel.Debug => "DBG",
        AppLogLevel.Info => "INF",
        AppLogLevel.Warning => "WRN",
        AppLogLevel.Error => "ERR",
        _ => "???"
    };

    private static ConsoleColor GetLevelColor(AppLogLevel level) => level switch
    {
        AppLogLevel.Debug => ConsoleColor.DarkGray,
        AppLogLevel.Info => ConsoleColor.Green,
        AppLogLevel.Warning => ConsoleColor.Yellow,
        AppLogLevel.Error => ConsoleColor.Red,
        _ => ConsoleColor.White
    };
}
