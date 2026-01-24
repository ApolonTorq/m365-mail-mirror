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
        var htmlDirectory = Path.Combine(Fixture.TestOutputPath, "transformed");
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
        var mdDirectory = Path.Combine(Fixture.TestOutputPath, "transformed");
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
        var htmlDirectory = Path.Combine(Fixture.TestOutputPath, "transformed");
        var mdDirectory = Path.Combine(Fixture.TestOutputPath, "transformed");

        Directory.Exists(htmlDirectory).Should().BeTrue("HTML directory should be created");
        Directory.Exists(mdDirectory).Should().BeTrue("Markdown directory should be created");
        MarkCompleted();
    }

    #endregion

    #region Attachment Link Tests

    [SkippableFact]
    [TestDescription("HTML files contain links to extracted attachments")]
    public async Task TransformCommand_HtmlGeneration_IncludesAttachmentLinks()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange - First extract attachments
        using var attachmentConsole = CreateTestConsole();
        var attachmentCommand = new TransformCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath,
            Html = false,
            Markdown = false,
            Attachments = true,
            Force = true,
            Parallel = 2
        };
        await attachmentCommand.ExecuteAsync(attachmentConsole.Console);

        // Then generate HTML with force to include attachment links
        using var htmlConsole = CreateTestConsole();
        var htmlCommand = new TransformCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath,
            Html = true,
            Markdown = false,
            Attachments = false,
            Force = true,
            Parallel = 2
        };
        await htmlCommand.ExecuteAsync(htmlConsole.Console);

        // Assert - Find attachment files and verify links in HTML
        var attachmentsDirectory = Path.Combine(Fixture.TestOutputPath, "transformed");
        if (!Directory.Exists(attachmentsDirectory))
        {
            Output.WriteLine("No attachments directory found - test mailbox may not contain attachments");
            MarkCompleted();
            return;
        }

        var attachmentFiles = Directory.GetFiles(attachmentsDirectory, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".skipped", StringComparison.OrdinalIgnoreCase))
            .Where(f => f.Contains(Path.DirectorySeparatorChar + "attachments" + Path.DirectorySeparatorChar))
            .ToList();

        if (attachmentFiles.Count == 0)
        {
            Output.WriteLine("No non-skipped attachments found - test mailbox may not contain valid attachments");
            MarkCompleted();
            return;
        }

        var htmlDirectory = Path.Combine(Fixture.TestOutputPath, "transformed");
        var verifiedCount = 0;

        // Check up to 3 attachments for links in corresponding HTML files
        foreach (var attachmentFile in attachmentFiles.Take(3))
        {
            var attachmentRelativePath = Path.GetRelativePath(Fixture.TestOutputPath, attachmentFile);
            var attachmentFileName = Path.GetFileName(attachmentFile);

            // Parse path: transformed/{folder...}/{YYYY}/{MM}/attachments/{message}_attachments/{file}
            // Note: folder can have multiple levels (e.g., "Inbox/Investors")
            var pathParts = attachmentRelativePath.Split(Path.DirectorySeparatorChar);
            var attachmentsIndex = Array.IndexOf(pathParts, "attachments");

            // Path must be: transformed/{folder...}/{YYYY}/{MM}/attachments/{message}_attachments/{file}
            // So attachments index must be at least 4 (transformed + folder + year + month + attachments)
            if (attachmentsIndex >= 4 && pathParts[0] == "transformed" && attachmentsIndex + 2 < pathParts.Length)
            {
                var yearIndex = attachmentsIndex - 2;
                var monthIndex = attachmentsIndex - 1;
                var year = pathParts[yearIndex];
                var month = pathParts[monthIndex];
                var messageDir = pathParts[attachmentsIndex + 1]; // e.g., "Message_1030_attachments"

                // Build folder path (everything between "transformed" and year/month)
                var folderParts = pathParts.Skip(1).Take(attachmentsIndex - 3).ToArray();
                var folder = string.Join(Path.DirectorySeparatorChar, folderParts);

                var messageBaseName = messageDir;

                // Find matching HTML file in corresponding location
                var htmlSearchDir = Path.Combine(htmlDirectory, folder, year, month);
                if (Directory.Exists(htmlSearchDir))
                {
                    var matchingHtmlFiles = Directory.GetFiles(htmlSearchDir, "*.html")
                        .Where(f => messageBaseName.StartsWith(
                            Path.GetFileNameWithoutExtension(f),
                            StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var htmlFile in matchingHtmlFiles)
                    {
                        var htmlContent = await File.ReadAllTextAsync(htmlFile);

                        // Check that HTML contains the attachment filename
                        if (htmlContent.Contains(attachmentFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            // Verify it's actually in an href link
                            htmlContent.Should().Contain("href=",
                                $"HTML file {htmlFile} should contain href attribute for attachment links");

                            verifiedCount++;
                            Output.WriteLine($"Verified attachment link: {attachmentFileName} in {Path.GetFileName(htmlFile)}");
                            break;
                        }
                    }
                }
            }
        }

        if (attachmentFiles.Count > 0)
        {
            verifiedCount.Should().BeGreaterThan(0,
                "At least one attachment link should be verified when attachments exist in test mailbox");
        }

        Output.WriteLine($"Total verified HTML attachment links: {verifiedCount}");
        MarkCompleted();
    }

    [SkippableFact]
    [TestDescription("Markdown files contain links to extracted attachments")]
    public async Task TransformCommand_MarkdownGeneration_IncludesAttachmentLinks()
    {
        TrackTest();
        Fixture.SkipIfNotAuthenticated();

        // Arrange - First extract attachments
        using var attachmentConsole = CreateTestConsole();
        var attachmentCommand = new TransformCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath,
            Html = false,
            Markdown = false,
            Attachments = true,
            Force = true,
            Parallel = 2
        };
        await attachmentCommand.ExecuteAsync(attachmentConsole.Console);

        // Then generate Markdown with force to include attachment links
        using var mdConsole = CreateTestConsole();
        var mdCommand = new TransformCommand
        {
            ConfigPath = Fixture.ConfigFilePath,
            ArchivePath = Fixture.TestOutputPath,
            Html = false,
            Markdown = true,
            Attachments = false,
            Force = true,
            Parallel = 2
        };
        await mdCommand.ExecuteAsync(mdConsole.Console);

        // Assert - Find attachment files and verify links in Markdown
        var attachmentsDirectory = Path.Combine(Fixture.TestOutputPath, "transformed");
        if (!Directory.Exists(attachmentsDirectory))
        {
            Output.WriteLine("No attachments directory found - test mailbox may not contain attachments");
            MarkCompleted();
            return;
        }

        var attachmentFiles = Directory.GetFiles(attachmentsDirectory, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".skipped", StringComparison.OrdinalIgnoreCase))
            .Where(f => f.Contains(Path.DirectorySeparatorChar + "attachments" + Path.DirectorySeparatorChar))
            .ToList();

        if (attachmentFiles.Count == 0)
        {
            Output.WriteLine("No non-skipped attachments found - test mailbox may not contain valid attachments");
            MarkCompleted();
            return;
        }

        var mdDirectory = Path.Combine(Fixture.TestOutputPath, "transformed");
        var verifiedCount = 0;

        // Check up to 3 attachments for links in corresponding Markdown files
        foreach (var attachmentFile in attachmentFiles.Take(3))
        {
            var attachmentRelativePath = Path.GetRelativePath(Fixture.TestOutputPath, attachmentFile);
            var attachmentFileName = Path.GetFileName(attachmentFile);

            // Parse path: transformed/{folder...}/{YYYY}/{MM}/attachments/{message}_attachments/{file}
            // Note: folder can have multiple levels (e.g., "Inbox/Investors")
            var pathParts = attachmentRelativePath.Split(Path.DirectorySeparatorChar);
            var attachmentsIndex = Array.IndexOf(pathParts, "attachments");

            // Path must be: transformed/{folder...}/{YYYY}/{MM}/attachments/{message}_attachments/{file}
            // So attachments index must be at least 4 (transformed + folder + year + month + attachments)
            if (attachmentsIndex >= 4 && pathParts[0] == "transformed" && attachmentsIndex + 2 < pathParts.Length)
            {
                var yearIndex = attachmentsIndex - 2;
                var monthIndex = attachmentsIndex - 1;
                var year = pathParts[yearIndex];
                var month = pathParts[monthIndex];
                var messageDir = pathParts[attachmentsIndex + 1]; // e.g., "Message_1030_attachments"

                // Build folder path (everything between "transformed" and year/month)
                var folderParts = pathParts.Skip(1).Take(attachmentsIndex - 3).ToArray();
                var folder = string.Join(Path.DirectorySeparatorChar, folderParts);

                var messageBaseName = messageDir;

                // Find matching Markdown file in corresponding location (same directory as HTML)
                var mdSearchDir = Path.Combine(mdDirectory, folder, year, month);
                if (Directory.Exists(mdSearchDir))
                {
                    var matchingMdFiles = Directory.GetFiles(mdSearchDir, "*.md")
                        .Where(f => messageBaseName.StartsWith(
                            Path.GetFileNameWithoutExtension(f),
                            StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var mdFile in matchingMdFiles)
                    {
                        var mdContent = await File.ReadAllTextAsync(mdFile);

                        // Check that Markdown contains the attachment filename
                        if (mdContent.Contains(attachmentFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            // Verify it's in Markdown link format [text](url)
                            mdContent.Should().MatchRegex(@"\[.*?\]\(.*?\)",
                                "Markdown file should contain link in [text](url) format");

                            verifiedCount++;
                            Output.WriteLine($"Verified attachment link: {attachmentFileName} in {Path.GetFileName(mdFile)}");
                            break;
                        }
                    }
                }
            }
        }

        if (attachmentFiles.Count > 0)
        {
            verifiedCount.Should().BeGreaterThan(0,
                "At least one attachment link should be verified when attachments exist in test mailbox");
        }

        Output.WriteLine($"Total verified Markdown attachment links: {verifiedCount}");
        MarkCompleted();
    }

    #endregion

    #region Index Generation Tests

    [SkippableFact]
    [TestDescription("Generates index files after HTML transformation")]
    public async Task TransformCommand_HtmlTransformation_GeneratesIndexFiles()
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
            Force = true,
            Parallel = 2
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Generating navigation indexes");
        stdout.Should().Contain("HTML indexes");

        // Verify root HTML index exists
        var rootHtmlIndex = Path.Combine(Fixture.TestOutputPath, "transformed", "index.html");
        File.Exists(rootHtmlIndex).Should().BeTrue("Root HTML index should be created");

        // Verify root index content
        var rootContent = await File.ReadAllTextAsync(rootHtmlIndex);
        rootContent.Should().Contain("Mail Archive");
        rootContent.Should().Contain("breadcrumb");
        rootContent.Should().Contain("Archive");

        Output.WriteLine($"Root HTML index verified at: {rootHtmlIndex}");
        MarkCompleted();
    }

    [SkippableFact]
    [TestDescription("Generates Markdown index files after Markdown transformation")]
    public async Task TransformCommand_MarkdownTransformation_GeneratesIndexFiles()
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
            Force = true,
            Parallel = 2
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert
        var stdout = console.ReadOutputString();
        stdout.Should().Contain("Generating navigation indexes");
        stdout.Should().Contain("Markdown indexes");

        // Verify root Markdown index exists
        var rootMdIndex = Path.Combine(Fixture.TestOutputPath, "transformed", "index.md");
        File.Exists(rootMdIndex).Should().BeTrue("Root Markdown index should be created");

        // Verify root index content
        var rootContent = await File.ReadAllTextAsync(rootMdIndex);
        rootContent.Should().Contain("Mail Archive");
        rootContent.Should().Contain("Archive");

        Output.WriteLine($"Root Markdown index verified at: {rootMdIndex}");
        MarkCompleted();
    }

    [SkippableFact]
    [TestDescription("Index files contain proper hierarchy structure")]
    public async Task TransformCommand_IndexGeneration_CreatesHierarchyStructure()
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
            Force = true,
            Parallel = 2
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert - Find all index files
        var htmlDirectory = Path.Combine(Fixture.TestOutputPath, "transformed");
        var indexFiles = Directory.GetFiles(htmlDirectory, "index.html", SearchOption.AllDirectories);

        indexFiles.Should().NotBeEmpty("At least one index file should exist");

        Output.WriteLine($"Found {indexFiles.Length} index files:");
        foreach (var indexFile in indexFiles)
        {
            var relativePath = Path.GetRelativePath(Fixture.TestOutputPath, indexFile);
            Output.WriteLine($"  - {relativePath}");

            // Each index should contain breadcrumb navigation
            var content = await File.ReadAllTextAsync(indexFile);
            content.Should().Contain("breadcrumb", $"Index file {relativePath} should contain breadcrumb navigation");
        }

        MarkCompleted();
    }

    #endregion

    #region Breadcrumb Navigation Tests

    [SkippableFact]
    [TestDescription("HTML email files contain breadcrumb navigation")]
    public async Task TransformCommand_HtmlFiles_ContainBreadcrumbNavigation()
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
            Force = true,
            Parallel = 2
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert - Find HTML email files (not index files)
        var htmlDirectory = Path.Combine(Fixture.TestOutputPath, "transformed");
        var htmlFiles = Directory.GetFiles(htmlDirectory, "*.html", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Equals("index.html", StringComparison.OrdinalIgnoreCase))
            .Take(5) // Check up to 5 files
            .ToList();

        htmlFiles.Should().NotBeEmpty("At least one HTML email file should exist");

        var verifiedCount = 0;
        foreach (var htmlFile in htmlFiles)
        {
            var content = await File.ReadAllTextAsync(htmlFile);
            var relativePath = Path.GetRelativePath(Fixture.TestOutputPath, htmlFile);

            // Each HTML file should contain breadcrumb navigation
            if (content.Contains("class=\"breadcrumb\""))
            {
                content.Should().Contain("Archive", $"Breadcrumb in {relativePath} should contain Archive link");
                verifiedCount++;
                Output.WriteLine($"Verified breadcrumb in: {relativePath}");
            }
        }

        verifiedCount.Should().BeGreaterThan(0, "At least one HTML email file should contain breadcrumb navigation");
        Output.WriteLine($"Total HTML files with breadcrumbs verified: {verifiedCount}");
        MarkCompleted();
    }

    [SkippableFact]
    [TestDescription("Markdown email files contain breadcrumb navigation")]
    public async Task TransformCommand_MarkdownFiles_ContainBreadcrumbNavigation()
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
            Force = true,
            Parallel = 2
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert - Find Markdown email files (not index files)
        var mdDirectory = Path.Combine(Fixture.TestOutputPath, "transformed");
        var mdFiles = Directory.GetFiles(mdDirectory, "*.md", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Equals("index.md", StringComparison.OrdinalIgnoreCase))
            .Take(5) // Check up to 5 files
            .ToList();

        mdFiles.Should().NotBeEmpty("At least one Markdown email file should exist");

        var verifiedCount = 0;
        foreach (var mdFile in mdFiles)
        {
            var content = await File.ReadAllTextAsync(mdFile);
            var relativePath = Path.GetRelativePath(Fixture.TestOutputPath, mdFile);

            // Each Markdown file should contain breadcrumb navigation with Archive link
            if (content.Contains("[Archive]"))
            {
                content.Should().Contain("index.md", $"Breadcrumb in {relativePath} should link to index files");
                verifiedCount++;
                Output.WriteLine($"Verified breadcrumb in: {relativePath}");
            }
        }

        verifiedCount.Should().BeGreaterThan(0, "At least one Markdown email file should contain breadcrumb navigation");
        Output.WriteLine($"Total Markdown files with breadcrumbs verified: {verifiedCount}");
        MarkCompleted();
    }

    [SkippableFact]
    [TestDescription("Month-level index contains email list with links")]
    public async Task TransformCommand_MonthIndex_ContainsEmailListWithLinks()
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
            Force = true,
            Parallel = 2
        };

        // Act
        await command.ExecuteAsync(console.Console);

        // Assert - Find a month-level index (e.g., html/Inbox/2024/01/index.html)
        var htmlDirectory = Path.Combine(Fixture.TestOutputPath, "transformed");
        var indexFiles = Directory.GetFiles(htmlDirectory, "index.html", SearchOption.AllDirectories);

        // Find deepest index files (month level typically has 4+ path segments: html/folder/year/month/index.html)
        var monthIndexes = indexFiles
            .Where(f =>
            {
                var relativePath = Path.GetRelativePath(Fixture.TestOutputPath, f);
                var depth = relativePath.Split(Path.DirectorySeparatorChar).Length;
                return depth >= 5; // html/folder/year/month/index.html
            })
            .Take(3)
            .ToList();

        if (monthIndexes.Count == 0)
        {
            Output.WriteLine("No month-level indexes found - archive may not have deep folder structure");
            MarkCompleted();
            return;
        }

        foreach (var monthIndex in monthIndexes)
        {
            var content = await File.ReadAllTextAsync(monthIndex);
            var relativePath = Path.GetRelativePath(Fixture.TestOutputPath, monthIndex);

            // Month index should contain email table with links
            content.Should().Contain("email-table", $"Month index {relativePath} should contain email table");
            content.Should().Contain(".html\"", $"Month index {relativePath} should contain links to HTML email files");

            Output.WriteLine($"Verified month index: {relativePath}");
        }

        MarkCompleted();
    }

    #endregion
}
