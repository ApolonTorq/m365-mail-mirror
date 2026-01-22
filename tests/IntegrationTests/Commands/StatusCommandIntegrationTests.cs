using M365MailMirror.Cli.Commands;
using Xunit.Abstractions;

namespace M365MailMirror.IntegrationTests.Commands;

/// <summary>
/// Integration tests for StatusCommand.
/// Uses shared fixture which has already performed initial sync.
/// </summary>
[Collection("IntegrationTests")]
[Trait("Category", "Integration")]
public class StatusCommandIntegrationTests
{
    private readonly IntegrationTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public StatusCommandIntegrationTests(IntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    #region Basic Status Tests

    [SkippableFact]
    public async Task StatusCommand_ShowsArchiveStatistics()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = new TestConsoleWrapper(_output);
        var command = new StatusCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = _fixture.TestOutputPath
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
    }

    [SkippableFact]
    public async Task StatusCommand_ShowsTransformationCounts()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = new TestConsoleWrapper(_output);
        var command = new StatusCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = _fixture.TestOutputPath
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Transformations:");
        stdout.Should().Contain("HTML:");
        stdout.Should().Contain("Markdown:");
        stdout.Should().Contain("Attachments:");
    }

    #endregion

    #region Verbose Mode Tests

    [SkippableFact]
    public async Task StatusCommand_VerboseMode_ShowsFolderDetails()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = new TestConsoleWrapper(_output);
        var command = new StatusCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = _fixture.TestOutputPath,
            Verbose = true
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Folder details:");
        stdout.Should().Contain("messages");
        stdout.Should().Contain("last sync:");
    }

    #endregion

    #region Quarantine Display Tests

    [SkippableFact]
    public async Task StatusCommand_QuarantineFlag_ShowsQuarantineSection()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = new TestConsoleWrapper(_output);
        var command = new StatusCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = _fixture.TestOutputPath,
            ShowQuarantine = true
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Quarantine:");
        // Note: May show "0 messages" if no quarantined items, which is valid
    }

    #endregion

    #region Edge Case Tests

    [SkippableFact]
    public async Task StatusCommand_NonExistentArchive_ShowsWarning()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange - Use a path that doesn't exist
        var nonExistentPath = Path.Combine(_fixture.TestOutputPath, "nonexistent");

        using var console = new TestConsoleWrapper(_output);
        var command = new StatusCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = nonExistentPath
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        var stderr = console.ReadErrorString();
        var allOutput = stdout + stderr;
        allOutput.Should().Contain("does not exist");
    }

    #endregion
}
