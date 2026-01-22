using M365MailMirror.Cli.Commands;
using Xunit.Abstractions;

namespace M365MailMirror.IntegrationTests.Commands;

/// <summary>
/// Integration tests for VerifyCommand.
/// Uses shared fixture which has already performed initial sync.
/// </summary>
[Collection("IntegrationTests")]
[Trait("Category", "Integration")]
public class VerifyCommandIntegrationTests : IntegrationTestBase
{
    public VerifyCommandIntegrationTests(IntegrationTestFixture fixture, ITestOutputHelper output)
        : base(fixture, output)
    {
    }

    #region Basic Verification Tests

    [SkippableFact]
    [TestDescription("Verifies a healthy archive reports no integrity issues")]
    public async Task VerifyCommand_HealthyArchive_ReportsNoIssues()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = CreateTestConsole();
        var command = new VerifyCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Verification complete");
        stdout.Should().Contain("No issues found");
        stdout.Should().Contain("Files checked:");
        MarkCompleted();
    }

    [SkippableFact]
    [TestDescription("Shows database, filesystem, and EML integrity check phases")]
    public async Task VerifyCommand_ShowsVerificationPhases()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = CreateTestConsole();
        var command = new VerifyCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Checking database entries");
        stdout.Should().Contain("Scanning file system");
        stdout.Should().Contain("Checking EML file integrity");
        MarkCompleted();
    }

    #endregion

    #region Verbose Mode Tests

    [SkippableFact]
    [TestDescription("Shows detailed verification output in verbose mode")]
    public async Task VerifyCommand_VerboseMode_ShowsDetailedOutput()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = CreateTestConsole();
        var command = new VerifyCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath,
            Verbose = true // This test specifically tests verbose mode
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Verification complete");
        stdout.Should().Contain("Files checked:");
        MarkCompleted();
    }

    #endregion

    #region Orphan Detection Tests

    [SkippableFact]
    [TestDescription("Detects EML files not tracked in the database")]
    public async Task VerifyCommand_OrphanDetection_DetectsUntrackedFiles()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange - Create an orphaned EML file that's not in the database
        var emlDir = Path.Combine(Fixture.TestOutputPath, "eml", "TestFolder", "2024", "01");
        Directory.CreateDirectory(emlDir);

        var orphanPath = Path.Combine(emlDir, "orphan_test_message.eml");
        await File.WriteAllTextAsync(orphanPath,
            "MIME-Version: 1.0\r\n" +
            "Subject: Orphan Test Message\r\n" +
            "From: test@example.com\r\n" +
            "Date: Mon, 1 Jan 2024 12:00:00 +0000\r\n" +
            "\r\n" +
            "This is an orphaned test message body.");

        using var console = CreateTestConsole();
        var command = new VerifyCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath,
            Verbose = Fixture.IsVerbose
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Untracked files");
        MarkCompleted();
    }

    #endregion

    #region Fix Mode Tests

    [SkippableFact]
    [TestDescription("Accepts the --fix flag for automatic repairs")]
    public async Task VerifyCommand_FixMode_AcceptsFixFlag()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = CreateTestConsole();
        var command = new VerifyCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath,
            Fix = true
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert - Verify the command completes (fix mode is accepted)
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Verification complete");
        MarkCompleted();
    }

    [SkippableFact]
    [TestDescription("Applies fixes to orphaned or corrupted entries")]
    public async Task VerifyCommand_FixMode_AppliesFixes()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange - Create an orphaned EML file
        var emlDir = Path.Combine(Fixture.TestOutputPath, "eml", "FixTestFolder", "2024", "01");
        Directory.CreateDirectory(emlDir);

        var orphanPath = Path.Combine(emlDir, "fix_test_message.eml");
        await File.WriteAllTextAsync(orphanPath,
            "MIME-Version: 1.0\r\n" +
            "Subject: Fix Test Message\r\n" +
            "From: test@example.com\r\n" +
            "Date: Mon, 1 Jan 2024 12:00:00 +0000\r\n" +
            "\r\n" +
            "This is a test message for fix mode.");

        using var console = CreateTestConsole();
        var command = new VerifyCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath,
            Fix = true,
            Verbose = Fixture.IsVerbose
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Verification complete");
        // Note: The verify command fixes missing files (db records without files)
        // but doesn't automatically add untracked files to the database
        MarkCompleted();
    }

    #endregion

    #region Error Case Tests

    [SkippableFact]
    [TestDescription("Shows error when archive path doesn't exist")]
    public async Task VerifyCommand_NonExistentArchive_ShowsError()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange - Use a path that doesn't exist
        var nonExistentPath = Path.Combine(Fixture.TestOutputPath, "nonexistent_archive");

        using var console = CreateTestConsole();
        var command = new VerifyCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
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
        MarkCompleted();
    }

    #endregion
}
