using FluentAssertions;
using M365MailMirror.Cli.Services;
using Xunit;

namespace M365MailMirror.UnitTests.Cli;

public class ResourceExtractorTests
{
    [Fact]
    public void GetAvailableResources_ReturnsEmbeddedResources()
    {
        // Act
        var resources = ResourceExtractor.GetAvailableResources();

        // Assert
        resources.Should().NotBeEmpty();
        resources.Should().Contain(r => r.Contains("CLAUDE.md"));
        resources.Should().Contain(r => r.Contains("copilotignore"));
        resources.Should().Contain(r => r.Contains("gitignore"));
        resources.Should().Contain(r => r.Contains("cursorignore"));
        resources.Should().Contain(r => r.Contains("vscode"));
    }

    [Fact]
    public void ExportAll_CreatesFiles()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();

        // Act
        var results = ResourceExtractor.ExportAll(tempDir.Path, overwrite: false);

        // Assert
        results.Should().NotBeEmpty();
        results.Should().HaveCount(5); // CLAUDE.md, .copilotignore, .cursorignore, .gitignore, .vscode/settings.json
        results.Should().Contain(r => r.RelativePath == "CLAUDE.md");
        results.Should().Contain(r => r.RelativePath == ".copilotignore");
        results.Should().Contain(r => r.RelativePath == ".cursorignore");
        results.Should().Contain(r => r.RelativePath == ".gitignore");
        results.Should().Contain(r => r.RelativePath == $".vscode{Path.DirectorySeparatorChar}settings.json");

        // Verify files were created
        File.Exists(Path.Combine(tempDir.Path, "CLAUDE.md")).Should().BeTrue();
        File.Exists(Path.Combine(tempDir.Path, ".copilotignore")).Should().BeTrue();
        File.Exists(Path.Combine(tempDir.Path, ".gitignore")).Should().BeTrue();
        File.Exists(Path.Combine(tempDir.Path, ".vscode", "settings.json")).Should().BeTrue();

        // Verify CLAUDE.md content
        var claudeFile = Path.Combine(tempDir.Path, "CLAUDE.md");
        var content = File.ReadAllText(claudeFile);
        content.Should().Contain("Mirrored Mailbox Archive");
        content.Length.Should().BeGreaterThan(100);
    }

    [Fact]
    public void ExportAll_WithOverwriteFalse_SkipsExistingFile()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var existingFilePath = Path.Combine(tempDir.Path, "CLAUDE.md");
        var originalContent = "original test content";
        File.WriteAllText(existingFilePath, originalContent);

        // Act
        var results = ResourceExtractor.ExportAll(tempDir.Path, overwrite: false);

        // Assert
        results.Should().HaveCount(5);
        var claudeResult = results.First(r => r.RelativePath == "CLAUDE.md");
        claudeResult.Status.Should().Be(ExportStatus.Skipped);

        var content = File.ReadAllText(existingFilePath);
        content.Should().Be(originalContent);
    }

    [Fact]
    public void ExportAll_WithOverwriteTrue_ReplacesExistingFile()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var existingFilePath = Path.Combine(tempDir.Path, "CLAUDE.md");
        var originalContent = "original test content";
        File.WriteAllText(existingFilePath, originalContent);

        // Act
        var results = ResourceExtractor.ExportAll(tempDir.Path, overwrite: true);

        // Assert
        results.Should().HaveCount(5);
        var claudeResult = results.First(r => r.RelativePath == "CLAUDE.md");
        claudeResult.Status.Should().Be(ExportStatus.Overwritten);

        var content = File.ReadAllText(existingFilePath);
        content.Should().NotBe(originalContent);
        content.Should().Contain("Mirrored Mailbox Archive");
    }

    [Fact]
    public void ExportAll_CreatesTargetDirectoryIfDoesNotExist()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var targetPath = Path.Combine(tempDir.Path, "subdir", "nested");
        Directory.Delete(tempDir.Path, recursive: true);

        // Act
        var results = ResourceExtractor.ExportAll(targetPath, overwrite: false);

        // Assert
        results.Should().NotBeEmpty();
        results.Should().HaveCount(5);
        Directory.Exists(targetPath).Should().BeTrue();
        File.Exists(Path.Combine(targetPath, "CLAUDE.md")).Should().BeTrue();
        File.Exists(Path.Combine(targetPath, ".gitignore")).Should().BeTrue();
        File.Exists(Path.Combine(targetPath, ".vscode", "settings.json")).Should().BeTrue();
    }
}

/// <summary>
/// Helper for creating temporary directories that are cleaned up automatically.
/// </summary>
internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; }

    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"m365-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
