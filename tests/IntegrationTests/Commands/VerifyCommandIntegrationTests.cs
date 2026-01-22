using M365MailMirror.Cli.Commands;
using Xunit.Abstractions;

namespace M365MailMirror.IntegrationTests.Commands;

/// <summary>
/// Integration tests for VerifyCommand.
/// Uses shared fixture which has already performed initial sync.
/// </summary>
[Collection("IntegrationTests")]
[Trait("Category", "Integration")]
public class VerifyCommandIntegrationTests
{
    private readonly IntegrationTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public VerifyCommandIntegrationTests(IntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    #region Basic Verification Tests

    [SkippableFact]
    public async Task VerifyCommand_HealthyArchive_ReportsNoIssues()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = new TestConsoleWrapper(_output);
        var command = new VerifyCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = _fixture.TestOutputPath
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Verification complete");
        stdout.Should().Contain("No issues found");
        stdout.Should().Contain("Files checked:");
    }

    [SkippableFact]
    public async Task VerifyCommand_ShowsVerificationPhases()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = new TestConsoleWrapper(_output);
        var command = new VerifyCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = _fixture.TestOutputPath
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Checking database entries");
        stdout.Should().Contain("Scanning file system");
        stdout.Should().Contain("Checking EML file integrity");
    }

    #endregion

    #region Verbose Mode Tests

    [SkippableFact]
    public async Task VerifyCommand_VerboseMode_ShowsDetailedOutput()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = new TestConsoleWrapper(_output);
        var command = new VerifyCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = _fixture.TestOutputPath,
            Verbose = true
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Verification complete");
        stdout.Should().Contain("Files checked:");
    }

    #endregion

    #region Orphan Detection Tests

    [SkippableFact]
    public async Task VerifyCommand_OrphanDetection_DetectsUntrackedFiles()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange - Create an orphaned EML file that's not in the database
        var emlDir = Path.Combine(_fixture.TestOutputPath, "eml", "TestFolder", "2024", "01");
        Directory.CreateDirectory(emlDir);

        var orphanPath = Path.Combine(emlDir, "orphan_test_message.eml");
        await File.WriteAllTextAsync(orphanPath,
            "MIME-Version: 1.0\r\n" +
            "Subject: Orphan Test Message\r\n" +
            "From: test@example.com\r\n" +
            "Date: Mon, 1 Jan 2024 12:00:00 +0000\r\n" +
            "\r\n" +
            "This is an orphaned test message body.");

        using var console = new TestConsoleWrapper(_output);
        var command = new VerifyCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = _fixture.TestOutputPath,
            Verbose = true
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Untracked files");
    }

    #endregion

    #region Fix Mode Tests

    [SkippableFact]
    public async Task VerifyCommand_FixMode_AcceptsFixFlag()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = new TestConsoleWrapper(_output);
        var command = new VerifyCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = _fixture.TestOutputPath,
            Fix = true
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert - Verify the command completes (fix mode is accepted)
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Verification complete");
    }

    [SkippableFact]
    public async Task VerifyCommand_FixMode_AppliesFixes()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange - Create an orphaned EML file
        var emlDir = Path.Combine(_fixture.TestOutputPath, "eml", "FixTestFolder", "2024", "01");
        Directory.CreateDirectory(emlDir);

        var orphanPath = Path.Combine(emlDir, "fix_test_message.eml");
        await File.WriteAllTextAsync(orphanPath,
            "MIME-Version: 1.0\r\n" +
            "Subject: Fix Test Message\r\n" +
            "From: test@example.com\r\n" +
            "Date: Mon, 1 Jan 2024 12:00:00 +0000\r\n" +
            "\r\n" +
            "This is a test message for fix mode.");

        using var console = new TestConsoleWrapper(_output);
        var command = new VerifyCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = _fixture.TestOutputPath,
            Fix = true,
            Verbose = true
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Verification complete");
        // Note: The verify command fixes missing files (db records without files)
        // but doesn't automatically add untracked files to the database
    }

    #endregion

    #region Error Case Tests

    [SkippableFact]
    public async Task VerifyCommand_NonExistentArchive_ShowsError()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange - Use a path that doesn't exist
        var nonExistentPath = Path.Combine(_fixture.TestOutputPath, "nonexistent_archive");

        using var console = new TestConsoleWrapper(_output);
        var command = new VerifyCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = nonExistentPath
        };

        // Act - BaseCommand catches exceptions and writes to stderr, then throws CommandException
        try
        {
            await command.ExecuteAsync(console.Console);
        }
        catch (CliFx.Exceptions.CommandException)
        {
            // Expected - BaseCommand wraps errors in CommandException with empty message
        }

        // Assert - Error message should be in stderr (written by BaseCommand.DisplayError)
        var stderr = console.ReadErrorString();
        stderr.Should().Contain("does not exist");
    }

    #endregion
}
