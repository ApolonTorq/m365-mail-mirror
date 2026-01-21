using M365MailMirror.Core.Exceptions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace M365MailMirror.Core.Configuration;

/// <summary>
/// Loads and merges configuration from YAML files, environment variables, and command-line overrides.
/// </summary>
public static class ConfigurationLoader
{
    private const string ConfigFileName = "config.yaml";
    private const string EnvPrefix = "M365_MAIL_MIRROR_";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    /// <summary>
    /// Gets the default configuration directory path.
    /// </summary>
    public static string GetDefaultConfigDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".config", "m365-mail-mirror");
    }

    /// <summary>
    /// Gets the default configuration file path.
    /// </summary>
    public static string GetDefaultConfigFilePath()
    {
        return Path.Combine(GetDefaultConfigDirectory(), ConfigFileName);
    }

    /// <summary>
    /// Gets the configuration file path in the current working directory.
    /// </summary>
    public static string GetWorkingDirectoryConfigFilePath()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), ConfigFileName);
    }

    /// <summary>
    /// Loads configuration from the specified file path, or from the default location if not specified.
    /// When no path is specified, checks for config.yaml in the current working directory first,
    /// then falls back to the user config directory (~/.config/m365-mail-mirror/config.yaml).
    /// Environment variables override file values.
    /// </summary>
    /// <param name="configPath">Optional path to configuration file.</param>
    /// <returns>The loaded configuration.</returns>
    public static AppConfiguration Load(string? configPath = null)
    {
        var config = new AppConfiguration();

        // Determine which file to load
        string? filePath = configPath;

        if (filePath == null)
        {
            // Check working directory first, then fall back to user config
            var workingDirConfig = GetWorkingDirectoryConfigFilePath();
            if (File.Exists(workingDirConfig))
            {
                filePath = workingDirConfig;
            }
            else
            {
                filePath = GetDefaultConfigFilePath();
            }
        }

        // Try to load from file
        if (File.Exists(filePath))
        {
            try
            {
                var yaml = File.ReadAllText(filePath);
                config = Deserializer.Deserialize<AppConfiguration>(yaml) ?? new AppConfiguration();
            }
            catch (YamlDotNet.Core.YamlException ex)
            {
                throw new ConfigurationException(
                    $"Invalid YAML syntax in configuration file: {ex.Message}", ex, filePath);
            }
        }

        // Apply environment variable overrides
        ApplyEnvironmentOverrides(config);

        return config;
    }

    /// <summary>
    /// Saves configuration to the specified file path, or to the default location if not specified.
    /// Creates the directory if it doesn't exist.
    /// </summary>
    /// <param name="config">The configuration to save.</param>
    /// <param name="configPath">Optional path to configuration file.</param>
    public static void Save(AppConfiguration config, string? configPath = null)
    {
        var filePath = configPath ?? GetDefaultConfigFilePath();
        var directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var yaml = Serializer.Serialize(config);
        File.WriteAllText(filePath, yaml);
    }

    /// <summary>
    /// Merges command-line overrides into the configuration.
    /// </summary>
    /// <param name="config">The configuration to update.</param>
    /// <param name="clientId">Optional client ID override.</param>
    /// <param name="tenantId">Optional tenant ID override.</param>
    /// <param name="outputPath">Optional output path override.</param>
    /// <param name="mailbox">Optional mailbox override.</param>
    /// <returns>The merged configuration.</returns>
    public static AppConfiguration MergeCommandLineOverrides(
        AppConfiguration config,
        string? clientId = null,
        string? tenantId = null,
        string? outputPath = null,
        string? mailbox = null)
    {
        if (!string.IsNullOrEmpty(clientId))
        {
            config.ClientId = clientId;
        }

        if (!string.IsNullOrEmpty(tenantId))
        {
            config.TenantId = tenantId;
        }

        if (!string.IsNullOrEmpty(outputPath))
        {
            config.OutputPath = outputPath;
        }

        if (!string.IsNullOrEmpty(mailbox))
        {
            config.Mailbox = mailbox;
        }

        return config;
    }

    /// <summary>
    /// Applies environment variable overrides to the configuration.
    /// Environment variables use the format: M365_MAIL_MIRROR_{SECTION}_{KEY}
    /// Examples:
    ///   M365_MAIL_MIRROR_CLIENT_ID
    ///   M365_MAIL_MIRROR_TENANT_ID
    ///   M365_MAIL_MIRROR_OUTPUT_PATH
    ///   M365_MAIL_MIRROR_SYNC_BATCH_SIZE
    /// </summary>
    private static void ApplyEnvironmentOverrides(AppConfiguration config)
    {
        // Root level properties
        ApplyEnvVar("CLIENT_ID", value => config.ClientId = value);
        ApplyEnvVar("TENANT_ID", value => config.TenantId = value);
        ApplyEnvVar("MAILBOX", value => config.Mailbox = value);
        ApplyEnvVar("OUTPUT_PATH", value => config.OutputPath = value);

        // Sync configuration
        ApplyEnvVarInt("SYNC_BATCH_SIZE", value => config.Sync.BatchSize = value);
        ApplyEnvVarInt("SYNC_PARALLEL", value => config.Sync.Parallel = value);
        ApplyEnvVarInt("SYNC_OVERLAP_MINUTES", value => config.Sync.OverlapMinutes = value);

        // Transform configuration
        ApplyEnvVarBool("TRANSFORM_GENERATE_HTML", value => config.Transform.GenerateHtml = value);
        ApplyEnvVarBool("TRANSFORM_GENERATE_MARKDOWN", value => config.Transform.GenerateMarkdown = value);
        ApplyEnvVarBool("TRANSFORM_EXTRACT_ATTACHMENTS", value => config.Transform.ExtractAttachments = value);

        // HTML transformation configuration
        ApplyEnvVarBool("TRANSFORM_HTML_INLINE_STYLES", value => config.Transform.Html.InlineStyles = value);
        ApplyEnvVarBool("TRANSFORM_HTML_STRIP_EXTERNAL_IMAGES", value => config.Transform.Html.StripExternalImages = value);
        ApplyEnvVarBool("TRANSFORM_HTML_HIDE_CC", value => config.Transform.Html.HideCc = value);
        ApplyEnvVarBool("TRANSFORM_HTML_HIDE_BCC", value => config.Transform.Html.HideBcc = value);

        // Attachment configuration
        ApplyEnvVarBool("ATTACHMENTS_SKIP_EXECUTABLES", value => config.Attachments.SkipExecutables = value);

        // ZIP extraction configuration
        ApplyEnvVarBool("ZIP_ENABLED", value => config.ZipExtraction.Enabled = value);
        ApplyEnvVarInt("ZIP_MIN_FILES", value => config.ZipExtraction.MinFiles = value);
        ApplyEnvVarInt("ZIP_MAX_FILES", value => config.ZipExtraction.MaxFiles = value);
        ApplyEnvVarBool("ZIP_SKIP_ENCRYPTED", value => config.ZipExtraction.SkipEncrypted = value);
        ApplyEnvVarBool("ZIP_SKIP_WITH_EXECUTABLES", value => config.ZipExtraction.SkipWithExecutables = value);
    }

    private static void ApplyEnvVar(string key, Action<string> setter)
    {
        var value = Environment.GetEnvironmentVariable(EnvPrefix + key);
        if (!string.IsNullOrEmpty(value))
        {
            setter(value);
        }
    }

    private static void ApplyEnvVarInt(string key, Action<int> setter)
    {
        var value = Environment.GetEnvironmentVariable(EnvPrefix + key);
        if (!string.IsNullOrEmpty(value) && int.TryParse(value, out var intValue))
        {
            setter(intValue);
        }
    }

    private static void ApplyEnvVarBool(string key, Action<bool> setter)
    {
        var value = Environment.GetEnvironmentVariable(EnvPrefix + key);
        if (!string.IsNullOrEmpty(value) && bool.TryParse(value, out var boolValue))
        {
            setter(boolValue);
        }
    }
}
