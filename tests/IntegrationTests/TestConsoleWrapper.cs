using CliFx.Infrastructure;
using M365MailMirror.Core.Logging;
using Xunit.Abstractions;

namespace M365MailMirror.IntegrationTests;

/// <summary>
/// Wraps ForwardingTestConsole to optionally output to xUnit's test output helper.
/// By default, forwards all output to the real console in real-time for visibility.
/// </summary>
public class TestConsoleWrapper : IDisposable
{
    private readonly ForwardingTestConsole _console;
    private readonly ITestOutputHelper? _output;

    /// <summary>
    /// Creates a new test console wrapper.
    /// </summary>
    /// <param name="output">Optional xUnit test output helper for test result logging.</param>
    /// <param name="forwardToConsole">If true (default), forwards output to real console in real-time.</param>
    public TestConsoleWrapper(ITestOutputHelper? output = null, bool forwardToConsole = true)
    {
        _console = new ForwardingTestConsole(forwardToConsole);
        _output = output;
    }

    /// <summary>
    /// Gets the underlying IConsole for passing to commands.
    /// </summary>
    public IConsole Console => _console;

    /// <summary>
    /// Gets all standard output as a string.
    /// Also writes to xUnit output if provided.
    /// </summary>
    public string ReadOutputString()
    {
        var output = _console.ReadOutputString();

        if (_output != null && !string.IsNullOrEmpty(output))
        {
            _output.WriteLine("[STDOUT]");
            _output.WriteLine(output);
        }

        return output;
    }

    /// <summary>
    /// Gets all error output as a string.
    /// Also writes to xUnit output if provided.
    /// </summary>
    public string ReadErrorString()
    {
        var error = _console.ReadErrorString();

        if (_output != null && !string.IsNullOrEmpty(error))
        {
            _output.WriteLine("[STDERR]");
            _output.WriteLine(error);
        }

        return error;
    }

    /// <summary>
    /// Gets all output (both stdout and stderr) for debugging.
    /// </summary>
    public string ReadAllOutput()
    {
        var stdout = ReadOutputString();
        var stderr = ReadErrorString();

        if (!string.IsNullOrEmpty(stderr))
        {
            return stdout + "\n[STDERR]\n" + stderr;
        }

        return stdout;
    }

    public void Dispose()
    {
        _console.Dispose();
        LoggerFactory.Reset(); // Clean up static state between tests
        GC.SuppressFinalize(this);
    }
}
