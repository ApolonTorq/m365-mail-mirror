using CliFx.Infrastructure;
using M365MailMirror.Cli.Commands;
using M365MailMirror.Infrastructure.Database;
using M365MailMirror.Infrastructure.Storage;
using FluentAssertions;
using Xunit;

namespace M365MailMirror.UnitTests.Cli;

public class VerifyCommandTests
{
    [Fact]
    public async Task ExecuteAsync_WithUntrackedEmlFile_DisplaysFilePathInOutput()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();

        var statusDirectory = Path.Combine(tempDir.Path, StateDatabase.DatabaseDirectory);
        Directory.CreateDirectory(statusDirectory);

        var databasePath = Path.Combine(statusDirectory, StateDatabase.DefaultDatabaseFilename);
        await using (var database = new StateDatabase(databasePath, logger: null))
        {
            await database.InitializeAsync();
        }

        var emlDirectory = Path.Combine(tempDir.Path, EmlStorageService.EmlDirectory);
        Directory.CreateDirectory(emlDirectory);

        // Create an untracked EML file
        var untrackedEmlPath = Path.Combine(emlDirectory, "Inbox", "untracked-message.eml");
        Directory.CreateDirectory(Path.GetDirectoryName(untrackedEmlPath)!);
        await File.WriteAllTextAsync(untrackedEmlPath, "This is an untracked EML file");

        using var console = new FakeInMemoryConsole();
        var command = new VerifyCommand
        {
            ArchivePath = tempDir.Path
        };

        // Act
        await command.ExecuteAsync(console);

        // Assert
        var output = console.ReadOutputString();

        // The untracked file path should be displayed
        output.Should().Contain("untracked-message.eml");
        output.Should().Contain("Untracked files");
    }
}
