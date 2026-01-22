using System.Text.Json;
using M365MailMirror.Core.Logging;

namespace M365MailMirror.IntegrationTests;

/// <summary>
/// Configuration settings for integration tests, loaded from integration-test-settings.jsonc.
/// </summary>
public sealed class TestSettings
{
    private const string SettingsFileName = "integration-test-settings.jsonc";

    /// <summary>
    /// The log level to use for test output.
    /// </summary>
    public AppLogLevel LogLevel { get; init; } = AppLogLevel.Info;

    /// <summary>
    /// The log file name for file output. If set, logs are written to this file
    /// in the status subfolder of the test output directory, in addition to console output.
    /// </summary>
    public string? LogFileName { get; init; }

    /// <summary>
    /// Loads test settings from integration-test-settings.jsonc in the IntegrationTests folder.
    /// </summary>
    /// <param name="projectRoot">The project root directory.</param>
    /// <returns>The loaded settings, or defaults if the file doesn't exist.</returns>
    public static TestSettings Load(string projectRoot)
    {
        var settingsPath = Path.Combine(projectRoot, "tests", "IntegrationTests", SettingsFileName);

        if (!File.Exists(settingsPath))
            return new TestSettings();

        try
        {
            var json = File.ReadAllText(settingsPath);
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip };
            var doc = JsonDocument.Parse(json, options);

            var logLevelStr = doc.RootElement.TryGetProperty("logLevel", out var prop)
                ? prop.GetString()
                : "info";

            var logLevel = logLevelStr?.ToLowerInvariant() switch
            {
                "debug" or "verbose" => AppLogLevel.Debug,
                "info" => AppLogLevel.Info,
                "warning" or "warn" => AppLogLevel.Warning,
                "error" => AppLogLevel.Error,
                "none" => AppLogLevel.None,
                _ => AppLogLevel.Info
            };

            var logFileName = doc.RootElement.TryGetProperty("logFileName", out var logFileProp)
                ? logFileProp.GetString()
                : null;

            return new TestSettings { LogLevel = logLevel, LogFileName = logFileName };
        }
        catch
        {
            // If parsing fails, return defaults
            return new TestSettings();
        }
    }
}
