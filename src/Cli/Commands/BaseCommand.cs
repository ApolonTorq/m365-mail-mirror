using CliFx;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using M365MailMirror.Core.Exceptions;

namespace M365MailMirror.Cli.Commands;

/// <summary>
/// Base command providing common functionality including error handling with optional stack traces.
/// All commands should extend this class to get consistent error handling behavior.
/// </summary>
public abstract class BaseCommand : ICommand
{
    private const string StacktraceEnvVar = "M365_MAIL_MIRROR_STACKTRACE";

    /// <summary>
    /// When set, shows full stack traces in error output instead of just the error message.
    /// </summary>
    [CommandOption("stacktrace", Description = "Show detailed error output including stack traces")]
    public bool ShowStackTrace { get; init; }

    /// <summary>
    /// Gets whether stack trace mode is enabled via flag or environment variable.
    /// </summary>
    protected bool IsStackTraceEnabled => ShowStackTrace || IsEnvironmentStackTraceEnabled();

    /// <summary>
    /// Executes the command with standardized error handling.
    /// We must use CommandException because CliFx intercepts all exceptions from commands.
    /// Non-CommandException types cause CliFx to display full stack traces automatically.
    /// </summary>
    public async ValueTask ExecuteAsync(IConsole console)
    {
        try
        {
            await ExecuteCommandAsync(console);
        }
        catch (OperationCanceledException)
        {
            DisplayCancellation(console);
            // Control character triggers CliFx's "has custom message" path
            // Using backspace (\b) which is invisible but not considered whitespace
            throw new CommandException("\b", CliExitCodes.Cancelled);
        }
        catch (M365MailMirrorException ex)
        {
            DisplayError(console, ex, isExpectedError: true);
            throw new CommandException("\b", ex.ExitCode);
        }
        catch (Exception ex)
        {
            DisplayError(console, ex, isExpectedError: false);
            throw new CommandException("\b", CliExitCodes.GeneralError);
        }
    }

    private static void DisplayCancellation(IConsole console)
    {
        console.ForegroundColor = ConsoleColor.Yellow;
        console.Error.WriteLine();
        console.Error.WriteLine("Operation cancelled by user.");
        console.ResetColor();
    }

    private void DisplayError(IConsole console, Exception ex, bool isExpectedError)
    {
        console.ForegroundColor = ConsoleColor.Red;

        if (IsStackTraceEnabled)
        {
            console.Error.WriteLine($"Error: {ex.Message}");
            console.Error.WriteLine();
            console.Error.WriteLine("Stack trace:");
            console.Error.WriteLine(ex.ToString());

            if (ex.InnerException != null)
            {
                console.Error.WriteLine();
                console.Error.WriteLine("Inner exception:");
                console.Error.WriteLine(ex.InnerException.ToString());
            }
        }
        else
        {
            console.Error.WriteLine($"Error: {ex.Message}");

            // Show hint about stack trace mode for unexpected errors
            if (!isExpectedError)
            {
                console.ResetColor();
                console.Error.WriteLine();
                console.Error.WriteLine($"Run with --stacktrace or set {StacktraceEnvVar}=true for more details.");
            }
        }

        console.ResetColor();
    }

    /// <summary>
    /// Override this method in derived classes to implement command-specific logic.
    /// Exceptions thrown from this method are caught and handled by the base class.
    /// </summary>
    /// <param name="console">The console to write output to.</param>
    protected abstract ValueTask ExecuteCommandAsync(IConsole console);

    /// <summary>
    /// Checks if stack trace mode is enabled via environment variable.
    /// </summary>
    private static bool IsEnvironmentStackTraceEnabled()
    {
        var envValue = Environment.GetEnvironmentVariable(StacktraceEnvVar);
        return !string.IsNullOrEmpty(envValue) &&
               (envValue.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                envValue.Equals("1", StringComparison.Ordinal));
    }

    /// <summary>
    /// Writes an error message to stderr in red.
    /// </summary>
    protected static async ValueTask WriteErrorAsync(IConsole console, string message)
    {
        console.ForegroundColor = ConsoleColor.Red;
        await console.Error.WriteLineAsync($"Error: {message}");
        console.ResetColor();
    }

    /// <summary>
    /// Writes a warning message to stdout in yellow.
    /// </summary>
    protected static async ValueTask WriteWarningAsync(IConsole console, string message)
    {
        console.ForegroundColor = ConsoleColor.Yellow;
        await console.Output.WriteLineAsync(message);
        console.ResetColor();
    }

    /// <summary>
    /// Writes a success message to stdout in green.
    /// </summary>
    protected static async ValueTask WriteSuccessAsync(IConsole console, string message)
    {
        console.ForegroundColor = ConsoleColor.Green;
        await console.Output.WriteLineAsync(message);
        console.ResetColor();
    }
}
