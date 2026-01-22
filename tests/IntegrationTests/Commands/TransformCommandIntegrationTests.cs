using M365MailMirror.Cli.Commands;
using Xunit.Abstractions;

namespace M365MailMirror.IntegrationTests.Commands;

/// <summary>
/// Integration tests for TransformCommand.
/// Uses shared fixture which has already performed initial sync with EML files.
/// </summary>
[Collection("IntegrationTests")]
[Trait("Category", "Integration")]
public class TransformCommandIntegrationTests : IntegrationTestBase
{
    public TransformCommandIntegrationTests(IntegrationTestFixture fixture, ITestOutputHelper output)
        : base(fixture, output)
    {
    }

    #region HTML Transformation Tests

    [SkippableFact]
    [TestDescription("Generates HTML files from EML archive")]
    public async Task TransformCommand_HtmlGeneration_CreatesHtmlFiles()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = CreateTestConsole();
        var command = new TransformCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath,
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
        var htmlDirectory = Path.Combine(Fixture.TestOutputPath, "html");
        Directory.Exists(htmlDirectory).Should().BeTrue("HTML directory should be created");

        var htmlFiles = Directory.GetFiles(htmlDirectory, "*.html", SearchOption.AllDirectories);
        htmlFiles.Should().NotBeEmpty("At least one HTML file should be generated");
        MarkCompleted();
    }

    #endregion

    #region Markdown Transformation Tests

    [SkippableFact]
    [TestDescription("Generates Markdown files from EML archive")]
    public async Task TransformCommand_MarkdownGeneration_CreatesMarkdownFiles()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = CreateTestConsole();
        var command = new TransformCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath,
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
        var mdDirectory = Path.Combine(Fixture.TestOutputPath, "markdown");
        Directory.Exists(mdDirectory).Should().BeTrue("Markdown directory should be created");

        var mdFiles = Directory.GetFiles(mdDirectory, "*.md", SearchOption.AllDirectories);
        mdFiles.Should().NotBeEmpty("At least one Markdown file should be generated");
        MarkCompleted();
    }

    #endregion

    #region Attachment Extraction Tests

    [SkippableFact]
    [TestDescription("Extracts attachments from emails")]
    public async Task TransformCommand_AttachmentExtraction_RunsSuccessfully()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = CreateTestConsole();
        var command = new TransformCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath,
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
        MarkCompleted();
    }

    #endregion

    #region Force Mode Tests

    [SkippableFact]
    [TestDescription("Regenerates all transformations with --force flag")]
    public async Task TransformCommand_ForceMode_RegeneratesAllTransformations()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange - First transform
        using var console1 = CreateTestConsole();
        var command1 = new TransformCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath,
            Html = true,
            Markdown = false,
            Attachments = false
        };
        await command1.ExecuteAsync(console1.Console);

        var stdout1 = console1.ReadOutputString();
        stdout1.Should().Contain("Transformation completed");

        // Arrange - Second transform with force
        using var console2 = CreateTestConsole();
        var command2 = new TransformCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath,
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
        MarkCompleted();
    }

    #endregion

    #region Only Filter Tests

    [SkippableFact]
    [TestDescription("Generates only HTML when --only html is specified")]
    public async Task TransformCommand_OnlyHtml_OnlyGeneratesHtml()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = CreateTestConsole();
        var command = new TransformCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath,
            Only = "html",
            Force = true // Force to ensure regeneration
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Only: html");
        stdout.Should().Contain("Transformation completed");
        MarkCompleted();
    }

    #endregion

    #region All Transformations Tests

    [SkippableFact]
    [TestDescription("Generates HTML, Markdown, and extracts attachments together")]
    public async Task TransformCommand_AllTransformations_GeneratesAllFormats()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange
        using var console = CreateTestConsole();
        var command = new TransformCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath,
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
        var htmlDirectory = Path.Combine(Fixture.TestOutputPath, "html");
        var mdDirectory = Path.Combine(Fixture.TestOutputPath, "markdown");

        Directory.Exists(htmlDirectory).Should().BeTrue("HTML directory should be created");
        Directory.Exists(mdDirectory).Should().BeTrue("Markdown directory should be created");
        MarkCompleted();
    }

    #endregion
}
