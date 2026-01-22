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
public class SyncCommandIntegrationTests : IntegrationTestBase
{
    public SyncCommandIntegrationTests(IntegrationTestFixture fixture, ITestOutputHelper output)
        : base(fixture, output)
    {
    }

    #region Initial Sync Tests

    [SkippableFact]
    [TestDescription("Downloads messages from Microsoft 365 and verifies EML files are created")]
    public async Task SyncCommand_InitialSync_DownloadsMessages()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = CreateTestConsole();
        var command = new SyncCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            OutputPath = Fixture.TestOutputPath,
            CheckpointInterval = 10, // Small interval for faster testing
            Parallel = 2,
            Verbose = Fixture.IsVerbose
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Authenticating");
        stdout.Should().Contain("Starting sync");
        stdout.Should().Contain("Sync completed successfully");

        // Verify EML directory was created
        var emlDirectory = Path.Combine(Fixture.TestOutputPath, "eml");
        Directory.Exists(emlDirectory).Should().BeTrue("EML directory should be created");

        // Verify database was created
        var dbPath = Fixture.GetDatabasePath();
        File.Exists(dbPath).Should().BeTrue("Database should be created");

        // Verify at least some messages were synced (can't predict exact count)
        await using var db = await Fixture.CreateDatabaseAsync();
        var messageCount = await db.GetMessageCountAsync();
        messageCount.Should().BeGreaterThan(0, "At least one message should be synced");

        // Verify output shows message counts
        stdout.Should().Contain("Messages synced:");
        MarkCompleted();
    }

    [SkippableFact]
    [TestDescription("Verifies dry run mode doesn't create any files")]
    public async Task SyncCommand_DryRun_NoFilesCreated()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Use isolated directory to ensure clean state (no files from other tests)
        var isolatedPath = Fixture.CreateIsolatedTestDirectory("DryRunTest");

        // Arrange
        using var console = CreateTestConsole();
        var command = new SyncCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            OutputPath = isolatedPath,
            CheckpointInterval = 5,
            DryRun = true,
            Verbose = Fixture.IsVerbose
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
        MarkCompleted();
    }

    #endregion

    #region Incremental Sync Tests

    [SkippableFact]
    [TestDescription("Verifies incremental sync skips already-downloaded messages")]
    public async Task SyncCommand_IncrementalSync_SkipsExistingMessages()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange - First sync
        using var console1 = CreateTestConsole();
        var command1 = new SyncCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            OutputPath = Fixture.TestOutputPath,
            CheckpointInterval = 5,
            Parallel = 2,
            Verbose = Fixture.IsVerbose
        };
        await command1.ExecuteAsync(console1.Console);

        // Verify first sync completed
        var stdout1 = console1.ReadOutputString();
        stdout1.Should().Contain("Sync completed successfully");

        // Get initial message count
        await using var db1 = await Fixture.CreateDatabaseAsync();
        var initialCount = await db1.GetMessageCountAsync();
        initialCount.Should().BeGreaterThan(0, "First sync should download some messages");

        // Arrange - Second sync (incremental)
        using var console2 = CreateTestConsole();
        var command2 = new SyncCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            OutputPath = Fixture.TestOutputPath,
            CheckpointInterval = 5,
            Parallel = 2,
            Verbose = Fixture.IsVerbose
        };

        // Act
        await command2.ExecuteAsync(console2.Console);

        // Assert
        var stdout2 = console2.ReadOutputString();
        stdout2.Should().Contain("Sync completed successfully");
        stdout2.Should().Contain("Messages skipped:");

        // Message count should be similar (may have new messages, but not significantly more)
        await using var db2 = await Fixture.CreateDatabaseAsync();
        var finalCount = await db2.GetMessageCountAsync();
        finalCount.Should().BeGreaterThanOrEqualTo(initialCount,
            "Message count should not decrease after incremental sync");
        MarkCompleted();
    }

    #endregion

    #region Folder Exclusion Tests

    [SkippableFact]
    [TestDescription("Verifies specified folders are excluded from sync")]
    public async Task SyncCommand_FolderExclusion_ExcludesSpecifiedFolders()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Use isolated directory to ensure clean state (no Inbox from initial fixture sync)
        var isolatedPath = Fixture.CreateIsolatedTestDirectory("FolderExclusionTest");

        // Arrange - Exclude Inbox to test exclusion
        using var console = CreateTestConsole();
        var command = new SyncCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            OutputPath = isolatedPath,
            CheckpointInterval = 5,
            ExcludeFolders = ["Inbox"], // Exclude Inbox
            Verbose = Fixture.IsVerbose
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
        MarkCompleted();
    }

    #endregion

    #region Transformation Flags Tests

    [SkippableFact]
    [TestDescription("Verifies HTML transformation runs during sync with --html flag")]
    public async Task SyncCommand_WithHtmlFlag_GeneratesHtmlDuringSync()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = CreateTestConsole();
        var command = new SyncCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            OutputPath = Fixture.TestOutputPath,
            CheckpointInterval = 5,
            Parallel = 2,
            GenerateHtml = true,
            Verbose = Fixture.IsVerbose
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Sync completed successfully");

        // Verify HTML files were generated (if any messages were synced)
        await using var db = await Fixture.CreateDatabaseAsync();
        var messageCount = await db.GetMessageCountAsync();

        if (messageCount > 0)
        {
            var htmlDirectory = Path.Combine(Fixture.TestOutputPath, "html");
            Directory.Exists(htmlDirectory).Should().BeTrue(
                "HTML directory should be created when --html flag is used");
        }
        MarkCompleted();
    }

    #endregion
}
