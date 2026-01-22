using System.Text;
using CliFx.Infrastructure;

namespace M365MailMirror.IntegrationTests;

/// <summary>
/// An IConsole implementation that captures output for assertions while also
/// forwarding all writes in real-time to the actual console and optionally to a log file.
/// Uses the decorator pattern via TeeStream.
/// </summary>
public class ForwardingTestConsole : IConsole, IDisposable
{
    private readonly MemoryStream _outputCapture;
    private readonly MemoryStream _errorCapture;
    private readonly MemoryStream _inputStream;

    /// <summary>
    /// Creates a console that captures output and forwards to the real console.
    /// </summary>
    /// <param name="forwardToConsole">If true, forwards output to Console.Out/Error in real-time.</param>
    /// <param name="logFileStream">Optional stream to also write all output to (e.g., for log files).</param>
    public ForwardingTestConsole(bool forwardToConsole = true, Stream? logFileStream = null)
    {
        _inputStream = new MemoryStream();
        _outputCapture = new MemoryStream();
        _errorCapture = new MemoryStream();

        Stream outputStream;
        Stream errorStream;

        if (forwardToConsole)
        {
            var outputSecondaries = new List<Stream> { Console.OpenStandardOutput() };
            var errorSecondaries = new List<Stream> { Console.OpenStandardError() };

            if (logFileStream != null)
            {
                // Wrap to prevent TeeStream from disposing the shared log file stream
                var nonDisposing = new NonDisposingStream(logFileStream);
                outputSecondaries.Add(nonDisposing);
                errorSecondaries.Add(nonDisposing);
            }

            outputStream = new TeeStream(_outputCapture, outputSecondaries.ToArray());
            errorStream = new TeeStream(_errorCapture, errorSecondaries.ToArray());
        }
        else
        {
            if (logFileStream != null)
            {
                var nonDisposing = new NonDisposingStream(logFileStream);
                outputStream = new TeeStream(_outputCapture, nonDisposing);
                errorStream = new TeeStream(_errorCapture, nonDisposing);
            }
            else
            {
                outputStream = _outputCapture;
                errorStream = _errorCapture;
            }
        }

        Input = new ConsoleReader(this, _inputStream);
        Output = new ConsoleWriter(this, outputStream);
        Error = new ConsoleWriter(this, errorStream);
    }

    /// <inheritdoc />
    public ConsoleReader Input { get; }

    /// <inheritdoc />
    public bool IsInputRedirected => true;

    /// <inheritdoc />
    public ConsoleWriter Output { get; }

    /// <inheritdoc />
    public bool IsOutputRedirected => false;

    /// <inheritdoc />
    public ConsoleWriter Error { get; }

    /// <inheritdoc />
    public bool IsErrorRedirected => false;

    /// <inheritdoc />
    public ConsoleColor ForegroundColor { get; set; } = Console.ForegroundColor;

    /// <inheritdoc />
    public ConsoleColor BackgroundColor { get; set; } = Console.BackgroundColor;

    /// <inheritdoc />
    public int WindowWidth { get; set; } = 120;

    /// <inheritdoc />
    public int WindowHeight { get; set; } = 30;

    /// <inheritdoc />
    public int CursorLeft { get; set; }

    /// <inheritdoc />
    public int CursorTop { get; set; }

    /// <inheritdoc />
    public ConsoleKeyInfo ReadKey(bool intercept = false) =>
        new('?', ConsoleKey.NoName, false, false, false);

    /// <inheritdoc />
    public void ResetColor()
    {
        ForegroundColor = ConsoleColor.Gray;
        BackgroundColor = ConsoleColor.Black;
    }

    /// <inheritdoc />
    public void Clear()
    {
        // No-op for test console
    }

    /// <inheritdoc />
    public CancellationToken RegisterCancellationHandler() => CancellationToken.None;

    /// <summary>
    /// Gets all captured standard output as a string.
    /// </summary>
    public string ReadOutputString()
    {
        Output.Flush();
        return Encoding.UTF8.GetString(_outputCapture.ToArray());
    }

    /// <summary>
    /// Gets all captured error output as a string.
    /// </summary>
    public string ReadErrorString()
    {
        Error.Flush();
        return Encoding.UTF8.GetString(_errorCapture.ToArray());
    }

    /// <summary>
    /// Gets all captured output (both stdout and stderr).
    /// </summary>
    public string ReadAllOutput()
    {
        var stdout = ReadOutputString();
        var stderr = ReadErrorString();
        return string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\n[STDERR]\n{stderr}";
    }

    public void Dispose()
    {
        Output.Dispose();
        Error.Dispose();
        Input.Dispose();
        _inputStream.Dispose();
        _outputCapture.Dispose();
        _errorCapture.Dispose();
        GC.SuppressFinalize(this);
    }
}
