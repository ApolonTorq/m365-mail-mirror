using System.Reflection;

namespace M365MailMirror.Cli.Services;

/// <summary>
/// Exports embedded resources from the CLI assembly to a target directory.
/// </summary>
public class ResourceExtractor
{
    private const string ResourceNamespace = "M365MailMirror.Cli.Resources";

    /// <summary>
    /// Gets list of all available embedded resources.
    /// </summary>
    /// <returns>List of resource names</returns>
    public static IReadOnlyList<string> GetAvailableResources()
    {
        var assembly = typeof(ResourceExtractor).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();

        return resourceNames
            .Where(r => r.StartsWith(ResourceNamespace, StringComparison.Ordinal))
            .OrderBy(r => r)
            .ToList();
    }

    /// <summary>
    /// Exports all embedded resources to the specified directory.
    /// </summary>
    /// <param name="targetDirectory">Directory to export resources to</param>
    /// <param name="overwrite">If true, overwrites existing files; if false, skips them</param>
    /// <returns>List of exported resource information</returns>
    public static IReadOnlyList<ExportedResource> ExportAll(string targetDirectory, bool overwrite = false)
    {
        // Create target directory if it doesn't exist
        if (!Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        var assembly = typeof(ResourceExtractor).Assembly;
        var resourceNames = GetAvailableResources();
        var exportedResources = new List<ExportedResource>();

        foreach (var resourceName in resourceNames)
        {
            var relativePath = GetRelativePath(resourceName);
            var fullPath = Path.Combine(targetDirectory, relativePath);

            // Create subdirectories if needed
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Check if file already exists
            var fileExists = File.Exists(fullPath);
            ExportStatus status;

            if (fileExists && !overwrite)
            {
                // Skip this file
                status = ExportStatus.Skipped;
                exportedResources.Add(new ExportedResource(
                    ResourceName: resourceName,
                    RelativePath: relativePath,
                    FullPath: fullPath,
                    Status: status));
                continue;
            }

            // Export the resource to disk
            using (var resourceStream = assembly.GetManifestResourceStream(resourceName))
            {
                if (resourceStream == null)
                {
                    throw new InvalidOperationException($"Resource stream not found for {resourceName}");
                }

                using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
                {
                    resourceStream.CopyTo(fileStream);
                }
            }

            // Determine if file was overwritten or newly created
            status = fileExists && overwrite ? ExportStatus.Overwritten : ExportStatus.Created;
            exportedResources.Add(new ExportedResource(
                ResourceName: resourceName,
                RelativePath: relativePath,
                FullPath: fullPath,
                Status: status));
        }

        return exportedResources;
    }

    /// <summary>
    /// Transforms a resource name to a relative file path.
    /// E.g., "M365MailMirror.Cli.Resources.CLAUDE.md" -> "CLAUDE.md"
    /// E.g., "M365MailMirror.Cli.Resources..vscode.settings.json" -> ".vscode/settings.json"
    /// E.g., "M365MailMirror.Cli.Resources.Skills.foo.md" -> "Skills/foo.md"
    /// </summary>
    private static string GetRelativePath(string resourceName)
    {
        // Remove the namespace prefix
        var prefix = ResourceNamespace + ".";
        if (!resourceName.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Resource name '{resourceName}' does not start with expected prefix '{prefix}'");
        }

        var relativePathPart = resourceName[prefix.Length..];

        // Handle files with no extension (e.g., ".gitignore", ".copilotignore")
        if (!relativePathPart.Contains('.'))
        {
            return relativePathPart;
        }

        // Preserve leading dots (for files like .vscode, .gitignore)
        var leadingDotCount = 0;
        for (int i = 0; i < relativePathPart.Length && relativePathPart[i] == '.'; i++)
        {
            leadingDotCount++;
        }

        var leadingDots = new string('.', leadingDotCount);
        var withoutLeadingDots = relativePathPart[leadingDotCount..];

        // If nothing after leading dots, the whole thing is a filename with leading dots
        if (string.IsNullOrEmpty(withoutLeadingDots))
        {
            return relativePathPart;
        }

        // Find the file extension - look for known patterns
        // For files like "settings.json", "CLAUDE.md"
        var knownExtensions = new[] { ".md", ".json", ".gitignore", ".copilotignore", ".cursorignore" };
        string? extensionPart = null;
        int extensionIndex = -1;

        foreach (var ext in knownExtensions)
        {
            if (withoutLeadingDots.EndsWith(ext, StringComparison.Ordinal))
            {
                extensionIndex = withoutLeadingDots.Length - ext.Length;
                extensionPart = withoutLeadingDots[extensionIndex..];
                break;
            }
        }

        // If no known extension found, fall back to treating the last dot as the extension separator
        if (extensionPart == null)
        {
            var lastDotIndex = withoutLeadingDots.LastIndexOf('.');
            if (lastDotIndex <= 0)
            {
                // No proper extension found
                return relativePathPart;
            }

            extensionIndex = lastDotIndex;
            extensionPart = withoutLeadingDots[extensionIndex..];
        }

        // Everything before the extension is the directory path
        var directoryPart = withoutLeadingDots[..extensionIndex];

        // Replace dots with path separators in the directory part
        var pathSeparator = Path.DirectorySeparatorChar.ToString();
        var pathifiedDirectory = directoryPart.Replace(".", pathSeparator);

        return leadingDots + pathifiedDirectory + extensionPart;
    }
}

/// <summary>
/// Information about an exported resource.
/// </summary>
/// <param name="ResourceName">Full resource name in assembly (e.g., "M365MailMirror.Cli.Resources.CLAUDE.md")</param>
/// <param name="RelativePath">Path relative to target directory (e.g., "CLAUDE.md")</param>
/// <param name="FullPath">Complete filesystem path to exported file</param>
/// <param name="Status">Status of the export: Created, Overwritten, or Skipped</param>
public record ExportedResource(
    string ResourceName,
    string RelativePath,
    string FullPath,
    ExportStatus Status);

/// <summary>
/// Status of resource export.
/// </summary>
public enum ExportStatus
{
    /// <summary>New file was created.</summary>
    Created,
    /// <summary>Existing file was overwritten.</summary>
    Overwritten,
    /// <summary>Existing file was skipped (not overwritten).</summary>
    Skipped
}
