using System.Text.Json;
using System.Text.Json.Nodes;

namespace M365MailMirror.Cli.Services;

/// <summary>
/// Service for configuring PATH environment variable in settings.local.json.
/// Enables the m365-mail-mirror command to be available in the user's shell after export.
/// </summary>
public class PathConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Detects the directory where the m365-mail-mirror executable is located.
    /// </summary>
    /// <returns>The directory containing the m365-mail-mirror executable</returns>
    public static string GetToolDirectory()
    {
        // Get the directory of the executing assembly (the CLI executable)
        var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
        return Path.GetDirectoryName(assemblyLocation) ?? AppContext.BaseDirectory;
    }

    /// <summary>
    /// Generates a PATH entry that includes the tool's directory and preserves existing PATH.
    /// Uses Git Bash format (forward slashes, colon separator) for cross-platform compatibility.
    /// </summary>
    /// <param name="toolDirectory">The full path to the m365-mail-mirror executable directory</param>
    /// <param name="preserveExistingPath">If true, appends to $PATH; if false, replaces it</param>
    /// <returns>PATH string in format: /path/to/tool:$PATH</returns>
    public static string GeneratePathEntry(string toolDirectory, bool preserveExistingPath = true)
    {
        // Convert backslashes to forward slashes (Git Bash format)
        var gitBashPath = toolDirectory.Replace("\\", "/");

        // Always use colon separator for consistency with Git Bash/WSL
        if (preserveExistingPath)
        {
            return $"{gitBashPath}:$PATH";
        }

        return gitBashPath;
    }

    /// <summary>
    /// Updates or creates settings.local.json in the specified archive directory.
    /// Merges with existing settings to preserve permissions and other configurations.
    /// </summary>
    /// <param name="archiveDirectory">The directory containing the .claude folder</param>
    /// <param name="pathEntry">The PATH entry to set (e.g., "/path/to/archive:$PATH")</param>
    public static void UpdateSettingsLocalJson(string archiveDirectory, string pathEntry)
    {
        var claudeDir = Path.Combine(archiveDirectory, ".claude");
        var settingsPath = Path.Combine(claudeDir, "settings.local.json");

        // Create .claude directory if it doesn't exist
        if (!Directory.Exists(claudeDir))
        {
            Directory.CreateDirectory(claudeDir);
        }

        // Read existing settings or create new structure
        JsonNode? settingsNode;

        if (File.Exists(settingsPath))
        {
            try
            {
                var existingJson = File.ReadAllText(settingsPath);
                settingsNode = JsonNode.Parse(existingJson);
            }
            catch (JsonException)
            {
                // If existing file is invalid JSON, start fresh
                settingsNode = new JsonObject();
            }
        }
        else
        {
            settingsNode = new JsonObject();
        }

        // Ensure it's an object
        if (settingsNode is not JsonObject settingsObj)
        {
            settingsObj = new JsonObject();
        }

        // Ensure env object exists
        if (settingsObj["env"] is not JsonObject envObj)
        {
            envObj = new JsonObject();
            settingsObj["env"] = envObj;
        }

        // Update or create PATH entry
        envObj["PATH"] = pathEntry;

        // Write back to file with nice formatting
        var jsonText = JsonSerializer.Serialize(settingsObj, JsonOptions);
        File.WriteAllText(settingsPath, jsonText);
    }
}
