using M365MailMirror.Cli.Commands;
using Xunit.Abstractions;

namespace M365MailMirror.IntegrationTests.Commands;

/// <summary>
/// Integration tests for TransformCommand.
/// Uses shared fixture which has already performed initial sync with EML files.
/// </summary>
[Collection("IntegrationTests")]
[Trait("Category", "Integration")]
public class TransformCommandIntegrationTests
{
    private readonly IntegrationTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public TransformCommandIntegrationTests(IntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    #region HTML Transformation Tests

    [SkippableFact]
    public async Task TransformCommand_HtmlGeneration_CreatesHtmlFiles()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = new TestConsoleWrapper(_output);
        var command = new TransformCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = _fixture.TestOutputPath,
            Html = true,
            Markdown = false,
            Attachments = false,
            Parallel = 2
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Transformation completed");

        // Verify HTML files created
        var htmlDirectory = Path.Combine(_fixture.TestOutputPath, "html");
        Directory.Exists(htmlDirectory).Should().BeTrue("HTML directory should be created");

        var htmlFiles = Directory.GetFiles(htmlDirectory, "*.html", SearchOption.AllDirectories);
        htmlFiles.Should().NotBeEmpty("At least one HTML file should be generated");
    }

    #endregion

    #region Markdown Transformation Tests

    [SkippableFact]
    public async Task TransformCommand_MarkdownGeneration_CreatesMarkdownFiles()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = new TestConsoleWrapper(_output);
        var command = new TransformCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = _fixture.TestOutputPath,
            Html = false,
            Markdown = true,
            Attachments = false,
            Parallel = 2
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Transformation completed");

        // Verify Markdown files created
        var mdDirectory = Path.Combine(_fixture.TestOutputPath, "markdown");
        Directory.Exists(mdDirectory).Should().BeTrue("Markdown directory should be created");

        var mdFiles = Directory.GetFiles(mdDirectory, "*.md", SearchOption.AllDirectories);
        mdFiles.Should().NotBeEmpty("At least one Markdown file should be generated");
    }

    #endregion

    #region Attachment Extraction Tests

    [SkippableFact]
    public async Task TransformCommand_AttachmentExtraction_RunsSuccessfully()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = new TestConsoleWrapper(_output);
        var command = new TransformCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = _fixture.TestOutputPath,
            Html = false,
            Markdown = false,
            Attachments = true,
            Parallel = 2
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Transformation completed");

        // Note: We can't assert attachment existence without knowing if test emails have attachments
        // The test verifies the command completes successfully
    }

    #endregion

    #region Force Mode Tests

    [SkippableFact]
    public async Task TransformCommand_ForceMode_RegeneratesAllTransformations()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange - First transform
        using var console1 = new TestConsoleWrapper(_output);
        var command1 = new TransformCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = _fixture.TestOutputPath,
            Html = true,
            Markdown = false,
            Attachments = false
        };
        await command1.ExecuteAsync(console1.Console);

        var stdout1 = console1.ReadOutputString();
        stdout1.Should().Contain("Transformation completed");

        // Arrange - Second transform with force
        using var console2 = new TestConsoleWrapper(_output);
        var command2 = new TransformCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = _fixture.TestOutputPath,
            Html = true,
            Markdown = false,
            Attachments = false,
            Force = true
        };

        // Act
        await command2.ExecuteAsync(console2.Console);

        // Assert
        var stdout2 = console2.ReadOutputString();
        stdout2.Should().Contain("Force mode");
        stdout2.Should().Contain("Transformation completed");
    }

    #endregion

    #region Only Filter Tests

    [SkippableFact]
    public async Task TransformCommand_OnlyHtml_OnlyGeneratesHtml()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = new TestConsoleWrapper(_output);
        var command = new TransformCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = _fixture.TestOutputPath,
            Only = "html",
            Force = true // Force to ensure regeneration
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Only: html");
        stdout.Should().Contain("Transformation completed");
    }

    #endregion

    #region All Transformations Tests

    [SkippableFact]
    public async Task TransformCommand_AllTransformations_GeneratesAllFormats()
    {
        _fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = new TestConsoleWrapper(_output);
        var command = new TransformCommand
        {
            ConfigPath = _fixture.ConfigFilePath,
            ArchivePath = _fixture.TestOutputPath,
            Html = true,
            Markdown = true,
            Attachments = true,
            Parallel = 2
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Transformation completed");

        // Verify both HTML and Markdown directories exist
        var htmlDirectory = Path.Combine(_fixture.TestOutputPath, "html");
        var mdDirectory = Path.Combine(_fixture.TestOutputPath, "markdown");

        Directory.Exists(htmlDirectory).Should().BeTrue("HTML directory should be created");
        Directory.Exists(mdDirectory).Should().BeTrue("Markdown directory should be created");
    }

    #endregion
}
