using M365MailMirror.Cli.Commands;
using Xunit.Abstractions;

namespace M365MailMirror.IntegrationTests.Commands;

/// <summary>
/// Integration tests for StatusCommand.
/// Uses shared fixture which has already performed initial sync.
/// </summary>
[Collection("IntegrationTests")]
[Trait("Category", "Integration")]
public class StatusCommandIntegrationTests : IntegrationTestBase
{
    public StatusCommandIntegrationTests(IntegrationTestFixture fixture, ITestOutputHelper output)
        : base(fixture, output)
    {
    }

    #region Basic Status Tests

    [SkippableFact]
    [TestDescription("Displays mailbox statistics including message and folder counts")]
    public async Task StatusCommand_ShowsArchiveStatistics()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = CreateTestConsole();
        var command = new StatusCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Mailbox:");
        stdout.Should().Contain("Last sync:");
        stdout.Should().Contain("Messages:");
        stdout.Should().Contain("Folders:");
        stdout.Should().Contain("Quarantine:");
        MarkCompleted();
    }

    [SkippableFact]
    [TestDescription("Shows counts for HTML, Markdown, and attachment transformations")]
    public async Task StatusCommand_ShowsTransformationCounts()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = CreateTestConsole();
        var command = new StatusCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Transformations:");
        stdout.Should().Contain("HTML:");
        stdout.Should().Contain("Markdown:");
        stdout.Should().Contain("Attachments:");
        MarkCompleted();
    }

    #endregion

    #region Verbose Mode Tests

    [SkippableFact]
    [TestDescription("Shows detailed folder information in verbose mode")]
    public async Task StatusCommand_VerboseMode_ShowsFolderDetails()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = CreateTestConsole();
        var command = new StatusCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath,
            Verbose = true // This test specifically tests verbose mode
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Folder details:");
        stdout.Should().Contain("messages");
        stdout.Should().Contain("last sync:");
        MarkCompleted();
    }

    #endregion

    #region Quarantine Display Tests

    [SkippableFact]
    [TestDescription("Displays quarantine section when --quarantine flag is used")]
    public async Task StatusCommand_QuarantineFlag_ShowsQuarantineSection()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = CreateTestConsole();
        var command = new StatusCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath,
            ShowQuarantine = true
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Quarantine:");
        // Note: May show "0 messages" if no quarantined items, which is valid
        MarkCompleted();
    }

    #endregion

    #region Edge Case Tests

    [SkippableFact]
    [TestDescription("Shows warning when archive path doesn't exist")]
    public async Task StatusCommand_NonExistentArchive_ShowsWarning()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange - Use a path that doesn't exist
        var nonExistentPath = Path.Combine(Fixture.TestOutputPath, "nonexistent");

        using var console = CreateTestConsole();
        var command = new StatusCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = nonExistentPath
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        var stderr = console.ReadErrorString();
        var allOutput = stdout + stderr;
        allOutput.Should().Contain("does not exist");
        MarkCompleted();
    }

    #endregion
}
