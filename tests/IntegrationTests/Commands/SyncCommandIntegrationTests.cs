using M365MailMirror.Cli.Commands;
using M365MailMirror.Infrastructure.Database;
using Xunit.Abstractions;

namespace M365MailMirror.IntegrationTests.Commands;

/// <summary>
/// Integration tests for SyncCommand against real Microsoft 365 backend.
/// Tests require pre-authentication via 'auth login'.
/// </summary>
[Collection("IntegrationTests")]
[Trait("Category", "Integration")]
public class SyncCommandIntegrationTests
{
    private readonly IntegrationTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SyncCommandIntegrationTests(IntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    #region Initial Sync Tests

    [SkippableFact]
    public async Task SyncCommand_InitialSync_DownloadsMessages()
    {
        // Skip if not authenticated
        _fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = new TestConsoleWrapper(_output);
        var command = new SyncCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            OutputPath = _fixture.TestOutputPath,
            CheckpointInterval = 10, // Small interval for faster testing
            Parallel = 2,
            Verbose = true // Enable verbose logging for integration tests
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Authenticating");
        stdout.Should().Contain("Starting sync");
        stdout.Should().Contain("Sync completed successfully");

        // Verify EML directory was created
        var emlDirectory = Path.Combine(_fixture.TestOutputPath, "eml");
        Directory.Exists(emlDirectory).Should().BeTrue("EML directory should be created");

        // Verify database was created
        var dbPath = _fixture.GetDatabasePath();
        File.Exists(dbPath).Should().BeTrue("Database should be created");

        // Verify at least some messages were synced (can't predict exact count)
        await using var db = await _fixture.CreateDatabaseAsync();
        var messageCount = await db.GetMessageCountAsync();
        messageCount.Should().BeGreaterThan(0, "At least one message should be synced");

        // Verify output shows message counts
        stdout.Should().Contain("Messages synced:");
    }

    [SkippableFact]
    public async Task SyncCommand_DryRun_NoFilesCreated()
    {
        _fixture.SkipIfNotAuthenticated();

        // Use isolated directory to ensure clean state (no files from other tests)
        var isolatedPath = _fixture.CreateIsolatedTestDirectory("DryRunTest");

        // Arrange
        using var console = new TestConsoleWrapper(_output);
        var command = new SyncCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            OutputPath = isolatedPath,
            CheckpointInterval = 5,
            DryRun = true,
            Verbose = true // Enable verbose logging for integration tests
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("DRY RUN MODE");

        // Verify no EML files were created
        var emlDirectory = Path.Combine(isolatedPath, "eml");
        if (Directory.Exists(emlDirectory))
        {
            var emlFiles = Directory.GetFiles(emlDirectory, "*.eml", SearchOption.AllDirectories);
            emlFiles.Should().BeEmpty("No EML files should be created in dry run mode");
        }
    }

    #endregion

    #region Incremental Sync Tests

    [SkippableFact]
    public async Task SyncCommand_IncrementalSync_SkipsExistingMessages()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange - First sync
        using var console1 = new TestConsoleWrapper(_output);
        var command1 = new SyncCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            OutputPath = _fixture.TestOutputPath,
            CheckpointInterval = 5,
            Parallel = 2,
            Verbose = true // Enable verbose logging for integration tests
        };
        await command1.ExecuteAsync(console1.Console);

        // Verify first sync completed
        var stdout1 = console1.ReadOutputString();
        stdout1.Should().Contain("Sync completed successfully");

        // Get initial message count
        await using var db1 = await _fixture.CreateDatabaseAsync();
        var initialCount = await db1.GetMessageCountAsync();
        initialCount.Should().BeGreaterThan(0, "First sync should download some messages");

        // Arrange - Second sync (incremental)
        using var console2 = new TestConsoleWrapper(_output);
        var command2 = new SyncCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            OutputPath = _fixture.TestOutputPath,
            CheckpointInterval = 5,
            Parallel = 2,
            Verbose = true // Enable verbose logging for integration tests
        };

        // Act
        await command2.ExecuteAsync(console2.Console);

        // Assert
        var stdout2 = console2.ReadOutputString();
        stdout2.Should().Contain("Sync completed successfully");
        stdout2.Should().Contain("Messages skipped:");

        // Message count should be similar (may have new messages, but not significantly more)
        await using var db2 = await _fixture.CreateDatabaseAsync();
        var finalCount = await db2.GetMessageCountAsync();
        finalCount.Should().BeGreaterThanOrEqualTo(initialCount,
            "Message count should not decrease after incremental sync");
    }

    #endregion

    #region Folder Exclusion Tests

    [SkippableFact]
    public async Task SyncCommand_FolderExclusion_ExcludesSpecifiedFolders()
    {
        _fixture.SkipIfNotAuthenticated();

        // Use isolated directory to ensure clean state (no Inbox from initial fixture sync)
        var isolatedPath = _fixture.CreateIsolatedTestDirectory("FolderExclusionTest");

        // Arrange - Exclude Inbox to test exclusion
        using var console = new TestConsoleWrapper(_output);
        var command = new SyncCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            OutputPath = isolatedPath,
            CheckpointInterval = 5,
            ExcludeFolders = ["Inbox"], // Exclude Inbox
            Verbose = true // Enable verbose logging for integration tests
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Excluded folders:");
        stdout.Should().Contain("Inbox");

        // Verify Inbox folder was not synced (no Inbox directory in eml/)
        var inboxPath = Path.Combine(isolatedPath, "eml", "Inbox");
        Directory.Exists(inboxPath).Should().BeFalse(
            "Inbox directory should not exist when folder is excluded");
    }

    #endregion

    #region Transformation Flags Tests

    [SkippableFact]
    public async Task SyncCommand_WithHtmlFlag_GeneratesHtmlDuringSync()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = new TestConsoleWrapper(_output);
        var command = new SyncCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            OutputPath = _fixture.TestOutputPath,
            CheckpointInterval = 5,
            Parallel = 2,
            GenerateHtml = true,
            Verbose = true // Enable verbose logging for integration tests
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Sync completed successfully");

        // Verify HTML files were generated (if any messages were synced)
        await using var db = await _fixture.CreateDatabaseAsync();
        var messageCount = await db.GetMessageCountAsync();

        if (messageCount > 0)
        {
            var htmlDirectory = Path.Combine(_fixture.TestOutputPath, "html");
            Directory.Exists(htmlDirectory).Should().BeTrue(
                "HTML directory should be created when --html flag is used");
        }
    }

    #endregion
}
