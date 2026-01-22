using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using M365MailMirror.Cli.Commands;
using M365MailMirror.Core.Exceptions;

namespace M365MailMirror.UnitTests.Cli;

public class BaseCommandTests
{
    [Fact]
    public async Task ExecuteAsync_WithException_DoesNotShowStackTraceByDefault()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new ThrowingCommand(new InvalidOperationException("Test error"));

        // Act
        var exception = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(console).AsTask());

        // Assert
        var output = console.ReadErrorString();
        output.Should().Contain("Error: Test error");
        output.Should().NotContain("Stack trace:");
        output.Should().NotContain("at M365MailMirror");
        output.Should().NotContain("InvalidOperationException");
        exception.ExitCode.Should().Be(CliExitCodes.GeneralError);
    }

    [Fact]
    public async Task ExecuteAsync_WithStackTraceFlag_ShowsStackTrace()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new ThrowingCommand(new InvalidOperationException("Test error"))
        {
            ShowStackTrace = true
        };

        // Act
        var exception = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(console).AsTask());

        // Assert
        var output = console.ReadErrorString();
        output.Should().Contain("Error: Test error");
        output.Should().Contain("Stack trace:");
        output.Should().Contain("InvalidOperationException");
        exception.ExitCode.Should().Be(CliExitCodes.GeneralError);
    }

    [Fact]
    public async Task ExecuteAsync_WithM365MailMirrorException_UsesCorrectExitCode()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new ThrowingCommand(
            new ConfigurationException("Config error", configFilePath: "/test/config.yaml"));

        // Act
        var exception = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(console).AsTask());

        // Assert
        var output = console.ReadErrorString();
        output.Should().Contain("Error: Config error");
        output.Should().NotContain("Stack trace:");
        exception.ExitCode.Should().Be(CliExitCodes.ConfigurationError);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnexpectedError_ShowsStackTraceHint()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new ThrowingCommand(new InvalidOperationException("Unexpected error"));

        // Act
        await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(console).AsTask());

        // Assert
        var output = console.ReadErrorString();
        output.Should().Contain("Run with --stacktrace");
        output.Should().Contain("M365_MAIL_MIRROR_STACKTRACE");
    }

    [Fact]
    public async Task ExecuteAsync_WithExpectedError_DoesNotShowStackTraceHint()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new ThrowingCommand(
            new M365MailMirrorException("Expected error", CliExitCodes.GeneralError));

        // Act
        await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(console).AsTask());

        // Assert
        var output = console.ReadErrorString();
        output.Should().Contain("Error: Expected error");
        output.Should().NotContain("Run with --stacktrace");
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_ShowsCancellationMessage()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new ThrowingCommand(new OperationCanceledException());

        // Act
        var exception = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(console).AsTask());

        // Assert
        var output = console.ReadErrorString();
        output.Should().Contain("Operation cancelled by user.");
        exception.ExitCode.Should().Be(CliExitCodes.Cancelled);
    }

    [Fact]
    public async Task ExecuteAsync_CommandExceptionHasInvisibleMessage_PreventsCliFxFromDuplicatingOutput()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new ThrowingCommand(new InvalidOperationException("Test error"));

        // Act
        var exception = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(console).AsTask());

        // Assert - CommandException should have backspace char to trigger CliFx's "has custom message" path
        // while remaining invisible to users. This prevents CliFx from displaying anything additional.
        exception.Message.Should().Be("\b");
    }

    /// <summary>
    /// Test command that throws a specified exception.
    /// </summary>
    [Command("test-throwing")]
    private sealed class ThrowingCommand : BaseCommand
    {
        private readonly Exception _exceptionToThrow;

        public ThrowingCommand(Exception exceptionToThrow)
        {
            _exceptionToThrow = exceptionToThrow;
        }

        protected override ValueTask ExecuteCommandAsync(IConsole console)
        {
            throw _exceptionToThrow;
        }
    }
}
