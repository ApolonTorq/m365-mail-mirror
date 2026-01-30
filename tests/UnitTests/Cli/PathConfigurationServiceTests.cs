using FluentAssertions;
using M365MailMirror.Cli.Services;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace M365MailMirror.UnitTests.Cli;

public class PathConfigurationServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [Fact]
    public void GetToolDirectory_ReturnsValidDirectory()
    {
        // Act
        var toolDir = PathConfigurationService.GetToolDirectory();

        // Assert
        toolDir.Should().NotBeNullOrEmpty();
        Directory.Exists(toolDir).Should().BeTrue();
    }

    [Fact]
    public void GeneratePathEntry_ConvertBackslashesToForwardSlashes()
    {
        // Arrange
        var windowsPath = @"C:\Users\testuser\archive";

        // Act
        var result = PathConfigurationService.GeneratePathEntry(windowsPath);

        // Assert
        result.Should().Be("C:/Users/testuser/archive:$PATH");
        result.Should().NotContain("\\");
    }

    [Fact]
    public void GeneratePathEntry_WithForwardSlashes_PreservesFormat()
    {
        // Arrange
        var unixPath = "/home/testuser/archive";

        // Act
        var result = PathConfigurationService.GeneratePathEntry(unixPath);

        // Assert
        result.Should().Be("/home/testuser/archive:$PATH");
    }

    [Fact]
    public void GeneratePathEntry_WithoutPreservePath_OmitsExistingPath()
    {
        // Arrange
        var path = "/home/testuser/archive";

        // Act
        var result = PathConfigurationService.GeneratePathEntry(path, preserveExistingPath: false);

        // Assert
        result.Should().Be("/home/testuser/archive");
        result.Should().NotContain("$PATH");
    }

    [Fact]
    public void GeneratePathEntry_PreservesPathToken()
    {
        // Arrange
        var path = "/home/testuser/archive";

        // Act
        var result = PathConfigurationService.GeneratePathEntry(path, preserveExistingPath: true);

        // Assert
        result.Should().EndWith(":$PATH");
        result.Should().Contain("$PATH");
    }

    [Fact]
    public void UpdateSettingsLocalJson_CreatesNewFileWhenNoneExists()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var exportPath = "/path/to/archive";
        var pathEntry = PathConfigurationService.GeneratePathEntry(exportPath);

        // Act
        PathConfigurationService.UpdateSettingsLocalJson(tempDir.Path, pathEntry);

        // Assert
        var settingsPath = Path.Combine(tempDir.Path, ".claude", "settings.local.json");
        File.Exists(settingsPath).Should().BeTrue();

        var content = File.ReadAllText(settingsPath);
        var json = JsonNode.Parse(content);
        json!["env"]!["PATH"]!.GetValue<string>().Should().Be("/path/to/archive:$PATH");
    }

    [Fact]
    public void UpdateSettingsLocalJson_PreservesExistingPermissions()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var claudeDir = Path.Combine(tempDir.Path, ".claude");
        Directory.CreateDirectory(claudeDir);

        var existingSettings = new JsonObject
        {
            ["permissions"] = new JsonObject
            {
                ["allow"] = new JsonArray("Read", "Write", "Edit")
            }
        };

        var settingsPath = Path.Combine(claudeDir, "settings.local.json");
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(existingSettings, JsonOptions));

        var pathEntry = "/path/to/archive:$PATH";

        // Act
        PathConfigurationService.UpdateSettingsLocalJson(tempDir.Path, pathEntry);

        // Assert
        var content = File.ReadAllText(settingsPath);
        var json = JsonNode.Parse(content);
        json!["env"]!["PATH"]!.GetValue<string>().Should().Be("/path/to/archive:$PATH");
        json["permissions"]!["allow"]!.AsArray().Count.Should().Be(3);
        json["permissions"]!["allow"]![0]!.GetValue<string>().Should().Be("Read");
    }

    [Fact]
    public void UpdateSettingsLocalJson_UpdatesExistingPathEntry()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var claudeDir = Path.Combine(tempDir.Path, ".claude");
        Directory.CreateDirectory(claudeDir);

        var existingSettings = new JsonObject
        {
            ["env"] = new JsonObject
            {
                ["PATH"] = "/old/path:$PATH"
            }
        };

        var settingsPath = Path.Combine(claudeDir, "settings.local.json");
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(existingSettings, JsonOptions));

        var newPathEntry = "/new/path:$PATH";

        // Act
        PathConfigurationService.UpdateSettingsLocalJson(tempDir.Path, newPathEntry);

        // Assert
        var content = File.ReadAllText(settingsPath);
        var json = JsonNode.Parse(content);
        json!["env"]!["PATH"]!.GetValue<string>().Should().Be("/new/path:$PATH");
    }

    [Fact]
    public void UpdateSettingsLocalJson_HandlesMalformedExistingJson()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var claudeDir = Path.Combine(tempDir.Path, ".claude");
        Directory.CreateDirectory(claudeDir);

        var settingsPath = Path.Combine(claudeDir, "settings.local.json");
        File.WriteAllText(settingsPath, "{ invalid json");

        var pathEntry = "/path/to/archive:$PATH";

        // Act
        PathConfigurationService.UpdateSettingsLocalJson(tempDir.Path, pathEntry);

        // Assert
        var content = File.ReadAllText(settingsPath);
        var json = JsonNode.Parse(content);
        json!["env"]!["PATH"]!.GetValue<string>().Should().Be("/path/to/archive:$PATH");
    }

    [Fact]
    public void UpdateSettingsLocalJson_CreatesCloudeDirectoryIfMissing()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var claudeDir = Path.Combine(tempDir.Path, ".claude");
        Directory.Exists(claudeDir).Should().BeFalse();

        var pathEntry = "/path/to/archive:$PATH";

        // Act
        PathConfigurationService.UpdateSettingsLocalJson(tempDir.Path, pathEntry);

        // Assert
        Directory.Exists(claudeDir).Should().BeTrue();
        File.Exists(Path.Combine(claudeDir, "settings.local.json")).Should().BeTrue();
    }

    [Fact]
    public void UpdateSettingsLocalJson_ProducesValidJson()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var pathEntry = "/path/to/archive:$PATH";

        // Act
        PathConfigurationService.UpdateSettingsLocalJson(tempDir.Path, pathEntry);

        // Assert
        var settingsPath = Path.Combine(tempDir.Path, ".claude", "settings.local.json");
        var content = File.ReadAllText(settingsPath);

        // This should not throw - if it does, JSON is invalid
        var json = JsonNode.Parse(content);
        json.Should().NotBeNull();
    }

    [Fact]
    public void UpdateSettingsLocalJson_IsIdempotent()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var pathEntry = "/path/to/archive:$PATH";

        // Act - call twice
        PathConfigurationService.UpdateSettingsLocalJson(tempDir.Path, pathEntry);
        var firstContent = File.ReadAllText(Path.Combine(tempDir.Path, ".claude", "settings.local.json"));

        PathConfigurationService.UpdateSettingsLocalJson(tempDir.Path, pathEntry);
        var secondContent = File.ReadAllText(Path.Combine(tempDir.Path, ".claude", "settings.local.json"));

        // Assert - content should be identical
        firstContent.Should().Be(secondContent);
    }

    [Fact]
    public void UpdateSettingsLocalJson_WithComplexPath_HandlesSpacesAndSpecialChars()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var complexPath = @"C:\Program Files\My App\bin";
        var pathEntry = PathConfigurationService.GeneratePathEntry(complexPath);

        // Act
        PathConfigurationService.UpdateSettingsLocalJson(tempDir.Path, pathEntry);

        // Assert
        var content = File.ReadAllText(Path.Combine(tempDir.Path, ".claude", "settings.local.json"));
        var json = JsonNode.Parse(content);
        json!["env"]!["PATH"]!.GetValue<string>().Should().Be("C:/Program Files/My App/bin:$PATH");
    }
}
