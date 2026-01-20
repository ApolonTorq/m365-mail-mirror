using M365MailMirror.Core.Configuration;

namespace M365MailMirror.UnitTests.Configuration;

public class ConfigurationLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Dictionary<string, string?> _originalEnvVars = new();

    public ConfigurationLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"m365_mail_mirror_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        // Restore original environment variables
        foreach (var (key, value) in _originalEnvVars)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        // Clean up temp directory
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Load_WithNoConfigFile_ReturnsDefaultConfiguration()
    {
        var configPath = Path.Combine(_tempDir, "nonexistent.yaml");

        var config = ConfigurationLoader.Load(configPath);

        config.Should().NotBeNull();
        config.TenantId.Should().Be("common");
        config.OutputPath.Should().Be(".");
        config.Sync.BatchSize.Should().Be(100);
        config.Sync.Parallel.Should().Be(5);
        config.Transform.GenerateHtml.Should().BeTrue();
        config.Transform.GenerateMarkdown.Should().BeFalse();
        config.Attachments.SkipExecutables.Should().BeTrue();
    }

    [Fact]
    public void Load_WithValidYamlFile_LoadsConfiguration()
    {
        var configPath = Path.Combine(_tempDir, "config.yaml");
        var yaml = """
            clientId: test-client-id
            tenantId: test-tenant-id
            outputPath: /archive/mail
            sync:
              batchSize: 50
              parallel: 10
            transform:
              generateHtml: true
              generateMarkdown: true
            """;
        File.WriteAllText(configPath, yaml);

        var config = ConfigurationLoader.Load(configPath);

        config.ClientId.Should().Be("test-client-id");
        config.TenantId.Should().Be("test-tenant-id");
        config.OutputPath.Should().Be("/archive/mail");
        config.Sync.BatchSize.Should().Be(50);
        config.Sync.Parallel.Should().Be(10);
        config.Transform.GenerateHtml.Should().BeTrue();
        config.Transform.GenerateMarkdown.Should().BeTrue();
    }

    [Fact]
    public void Load_WithEnvironmentVariableOverrides_AppliesOverrides()
    {
        var configPath = Path.Combine(_tempDir, "config.yaml");
        var yaml = """
            clientId: file-client-id
            tenantId: file-tenant-id
            """;
        File.WriteAllText(configPath, yaml);

        SetEnvVar("M365_MAIL_MIRROR_CLIENT_ID", "env-client-id");
        SetEnvVar("M365_MAIL_MIRROR_SYNC_BATCH_SIZE", "200");
        SetEnvVar("M365_MAIL_MIRROR_TRANSFORM_GENERATE_MARKDOWN", "true");

        var config = ConfigurationLoader.Load(configPath);

        // Environment variable should override file value
        config.ClientId.Should().Be("env-client-id");
        // File value should be preserved when no env var
        config.TenantId.Should().Be("file-tenant-id");
        // Integer env var should be parsed
        config.Sync.BatchSize.Should().Be(200);
        // Boolean env var should be parsed
        config.Transform.GenerateMarkdown.Should().BeTrue();
    }

    [Fact]
    public void MergeCommandLineOverrides_OverridesConfiguration()
    {
        var config = new AppConfiguration
        {
            ClientId = "original-client-id",
            TenantId = "original-tenant-id",
            OutputPath = "/original/path"
        };

        var merged = ConfigurationLoader.MergeCommandLineOverrides(
            config,
            clientId: "cli-client-id",
            outputPath: "/cli/path");

        merged.ClientId.Should().Be("cli-client-id");
        merged.TenantId.Should().Be("original-tenant-id"); // Not overridden
        merged.OutputPath.Should().Be("/cli/path");
    }

    [Fact]
    public void MergeCommandLineOverrides_WithNullValues_PreservesExisting()
    {
        var config = new AppConfiguration
        {
            ClientId = "original-client-id",
            TenantId = "original-tenant-id",
        };

        var merged = ConfigurationLoader.MergeCommandLineOverrides(
            config,
            clientId: null,
            tenantId: null);

        merged.ClientId.Should().Be("original-client-id");
        merged.TenantId.Should().Be("original-tenant-id");
    }

    [Fact]
    public void Save_CreatesConfigurationFile()
    {
        var configPath = Path.Combine(_tempDir, "new-config.yaml");
        var config = new AppConfiguration
        {
            ClientId = "saved-client-id",
            TenantId = "saved-tenant-id",
            OutputPath = "/saved/path",
            Sync = new SyncConfiguration { BatchSize = 75 }
        };

        ConfigurationLoader.Save(config, configPath);

        File.Exists(configPath).Should().BeTrue();
        var content = File.ReadAllText(configPath);
        content.Should().Contain("saved-client-id");
        content.Should().Contain("saved-tenant-id");
    }

    [Fact]
    public void Save_CreatesDirectoryIfNotExists()
    {
        var nestedDir = Path.Combine(_tempDir, "nested", "dir");
        var configPath = Path.Combine(nestedDir, "config.yaml");
        var config = new AppConfiguration();

        ConfigurationLoader.Save(config, configPath);

        Directory.Exists(nestedDir).Should().BeTrue();
        File.Exists(configPath).Should().BeTrue();
    }

    [Fact]
    public void GetDefaultConfigDirectory_ReturnsUserConfigPath()
    {
        var configDir = ConfigurationLoader.GetDefaultConfigDirectory();

        configDir.Should().Contain("m365-mail-mirror");
        configDir.Should().Contain(".config");
    }

    [Fact]
    public void GetDefaultConfigFilePath_ReturnsConfigYaml()
    {
        var configPath = ConfigurationLoader.GetDefaultConfigFilePath();

        configPath.Should().EndWith("config.yaml");
        configPath.Should().Contain("m365-mail-mirror");
    }

    [Fact]
    public void Load_WithPartialYamlFile_UsesDefaultsForMissingValues()
    {
        var configPath = Path.Combine(_tempDir, "partial.yaml");
        var yaml = """
            clientId: partial-client-id
            """;
        File.WriteAllText(configPath, yaml);

        var config = ConfigurationLoader.Load(configPath);

        config.ClientId.Should().Be("partial-client-id");
        config.TenantId.Should().Be("common"); // Default value
        config.Sync.BatchSize.Should().Be(100); // Default value
        config.Transform.GenerateHtml.Should().BeTrue(); // Default value
    }

    [Fact]
    public void Load_WithExcludeFolders_ParsesListCorrectly()
    {
        var configPath = Path.Combine(_tempDir, "folders.yaml");
        var yaml = """
            sync:
              excludeFolders:
                - Deleted Items
                - Junk Email
                - Drafts
            """;
        File.WriteAllText(configPath, yaml);

        var config = ConfigurationLoader.Load(configPath);

        config.Sync.ExcludeFolders.Should().HaveCount(3);
        config.Sync.ExcludeFolders.Should().Contain("Deleted Items");
        config.Sync.ExcludeFolders.Should().Contain("Junk Email");
        config.Sync.ExcludeFolders.Should().Contain("Drafts");
    }

    private void SetEnvVar(string key, string value)
    {
        // Save original value if not already saved
        if (!_originalEnvVars.ContainsKey(key))
        {
            _originalEnvVars[key] = Environment.GetEnvironmentVariable(key);
        }

        Environment.SetEnvironmentVariable(key, value);
    }
}
