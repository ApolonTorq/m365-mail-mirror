using CliFx.Exceptions;
using CliFx.Infrastructure;
using M365MailMirror.Cli.Commands;
using M365MailMirror.Cli.Services;
using FluentAssertions;
using System.Text.Json.Nodes;
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

    [Fact]
    public async Task ExecuteAsync_UpdatesPathInSettingsLocalJson()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        using var console = new FakeInMemoryConsole();
        var command = new ExportResourcesCommand
        {
            ArchivePath = tempDir.Path
        };

        var expectedToolDir = PathConfigurationService.GetToolDirectory();

        // Act
        await command.ExecuteAsync(console);

        // Assert
        var settingsPath = Path.Combine(tempDir.Path, ".claude", "settings.local.json");
        File.Exists(settingsPath).Should().BeTrue();

        var content = File.ReadAllText(settingsPath);
        var json = JsonNode.Parse(content);
        var pathValue = json!["env"]!["PATH"]!.GetValue<string>();

        // Verify tool directory is in the PATH
        pathValue.Should().StartWith(expectedToolDir.Replace("\\", "/"));
        pathValue.Should().Contain(":$PATH");

        var output = console.ReadOutputString();
        output.Should().Contain("Updated PATH");
        output.Should().Contain("settings.local.json");
    }

    [Fact]
    public async Task ExecuteAsync_PreservesExistingSettingsWhenUpdatingPath()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var claudeDir = Path.Combine(tempDir.Path, ".claude");
        Directory.CreateDirectory(claudeDir);

        // Create existing settings with permissions
        var existingSettings = """
        {
          "permissions": {
            "allow": ["Read", "Write"]
          }
        }
        """;
        var settingsPath = Path.Combine(claudeDir, "settings.local.json");
        File.WriteAllText(settingsPath, existingSettings);

        using var console = new FakeInMemoryConsole();
        var command = new ExportResourcesCommand
        {
            ArchivePath = tempDir.Path
        };

        // Act
        await command.ExecuteAsync(console);

        // Assert
        var updatedContent = File.ReadAllText(settingsPath);
        updatedContent.Should().Contain("\"permissions\"");
        updatedContent.Should().Contain("\"allow\"");
        updatedContent.Should().Contain("\"Read\"");
        updatedContent.Should().Contain("\"Write\"");
        updatedContent.Should().Contain("\"PATH\"");
    }

}
