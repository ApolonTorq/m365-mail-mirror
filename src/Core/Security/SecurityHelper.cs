namespace M365MailMirror.Core.Security;

/// <summary>
/// Security helper for filtering executables and validating paths.
/// </summary>
public static class SecurityHelper
{
    /// <summary>
    /// Blocked executable extensions.
    /// </summary>
    public static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows executables
        ".exe", ".dll", ".bat", ".cmd", ".msi", ".scr", ".com", ".pif", ".ps1", ".vbs", ".js", ".wsf", ".hta",
        // Scripts
        ".sh", ".bash", ".zsh", ".fish", ".csh", ".py", ".rb", ".pl", ".php",
        // Java/JVM
        ".jar", ".class", ".war", ".ear",
        // macOS
        ".app", ".dmg", ".pkg",
        // Linux packages
        ".deb", ".rpm", ".run", ".bin", ".AppImage",
        // Mobile
        ".apk", ".ipa"
    };

    /// <summary>
    /// Checks if a filename has a blocked executable extension.
    /// </summary>
    /// <param name="filename">The filename to check.</param>
    /// <returns>True if the file extension is blocked.</returns>
    public static bool IsExecutable(string filename)
    {
        var extension = Path.GetExtension(filename);
        return BlockedExtensions.Contains(extension);
    }

    /// <summary>
    /// Gets the blocked extension if present, or null if not blocked.
    /// </summary>
    /// <param name="filename">The filename to check.</param>
    /// <returns>The blocked extension, or null if allowed.</returns>
    public static string? GetBlockedExtension(string filename)
    {
        var extension = Path.GetExtension(filename);
        return BlockedExtensions.Contains(extension) ? extension : null;
    }

    /// <summary>
    /// Checks if a path contains path traversal sequences.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <returns>True if the path contains traversal sequences.</returns>
    public static bool HasPathTraversal(string path)
    {
        // Check for .. sequences
        if (path.Contains(".."))
            return true;

        // Normalize and check again
        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("../") || normalized.Contains("/.."))
            return true;

        return false;
    }

    /// <summary>
    /// Checks if a path is an absolute path (Windows or Unix).
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <returns>True if the path is absolute.</returns>
    public static bool IsAbsolutePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        // Check Windows absolute paths (C:\, D:\, etc.)
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
            return true;

        // Check UNC paths (\\server\share)
        if (path.StartsWith("\\\\", StringComparison.Ordinal))
            return true;

        // Check Unix absolute paths (/etc, /usr)
        if (path.StartsWith('/'))
            return true;

        return false;
    }

    /// <summary>
    /// Checks if a ZIP entry path is safe for extraction.
    /// </summary>
    /// <param name="entryPath">The ZIP entry path.</param>
    /// <returns>True if the path is safe.</returns>
    public static bool IsZipEntrySafe(string entryPath)
    {
        if (string.IsNullOrEmpty(entryPath))
            return false;

        // Check for absolute paths
        if (IsAbsolutePath(entryPath))
            return false;

        // Check for path traversal
        if (HasPathTraversal(entryPath))
            return false;

        return true;
    }
}
