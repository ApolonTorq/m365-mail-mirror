using CliFx.Exceptions;
using CliFx.Infrastructure;
using M365MailMirror.Cli.Commands;
using FluentAssertions;
using Xunit;

namespace M365MailMirror.UnitTests.Cli;

public class ExportResourcesCommandTests
{
    [Fact]
    public async Task ExecuteAsync_WithNonExistentOutputDirectory_ThrowsError()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new ExportResourcesCommand
        {
            ArchivePath = "/nonexistent/path/that/does/not/exist"
        };

        // Act & Assert
        await Assert.ThrowsAsync<CommandException>(async () => await command.ExecuteAsync(console));

        var error = console.ReadErrorString();
        error.Should().Contain("Error");
        error.Should().Contain("Output directory does not exist");
    }

    [Fact]
    public async Task ExecuteAsync_ExportsResourcesSuccessfully()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        using var console = new FakeInMemoryConsole();
        var command = new ExportResourcesCommand
        {
            ArchivePath = tempDir.Path
        };

        // Act
        await command.ExecuteAsync(console);

        // Assert
        var output = console.ReadOutputString();
        output.Should().Contain("Exported");
        output.Should().Contain("CLAUDE.md");
        output.Should().Contain("Created");

        File.Exists(Path.Combine(tempDir.Path, "CLAUDE.md")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithoutOverwrite_SkipsExistingFiles()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var existingFilePath = Path.Combine(tempDir.Path, "CLAUDE.md");
        var originalContent = "original test content";
        File.WriteAllText(existingFilePath, originalContent);

        using var console = new FakeInMemoryConsole();
        var command = new ExportResourcesCommand
        {
            ArchivePath = tempDir.Path,
            Overwrite = false
        };

        // Act
        await command.ExecuteAsync(console);

        // Assert
        var output = console.ReadOutputString();
        output.Should().Contain("Skipped");

        var content = File.ReadAllText(existingFilePath);
        content.Should().Be(originalContent);
    }

    [Fact]
    public async Task ExecuteAsync_WithOverwrite_ReplacesExistingFiles()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var existingFilePath = Path.Combine(tempDir.Path, "CLAUDE.md");
        var originalContent = "original test content";
        File.WriteAllText(existingFilePath, originalContent);

        using var console = new FakeInMemoryConsole();
        var command = new ExportResourcesCommand
        {
            ArchivePath = tempDir.Path,
            Overwrite = true
        };

        // Act
        await command.ExecuteAsync(console);

        // Assert
        var output = console.ReadOutputString();
        output.Should().Contain("Overwrote");

        var content = File.ReadAllText(existingFilePath);
        content.Should().NotBe(originalContent);
        content.Should().Contain("Mirrored Mailbox Archive");
    }

    [Fact]
    public async Task ExecuteAsync_WithArchiveOverride_UsesProvidedPath()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        using var console = new FakeInMemoryConsole();
        var command = new ExportResourcesCommand
        {
            ArchivePath = tempDir.Path
        };

        // Act
        await command.ExecuteAsync(console);

        // Assert
        var output = console.ReadOutputString();
        output.Should().Contain(tempDir.Path);
        File.Exists(Path.Combine(tempDir.Path, "CLAUDE.md")).Should().BeTrue();
    }
}
