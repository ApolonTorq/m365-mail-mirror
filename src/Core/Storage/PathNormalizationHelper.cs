using System.Text;

namespace M365MailMirror.Core.Storage;

/// <summary>
/// Helper class for normalizing file paths to ensure consistent Unicode handling
/// across database storage and filesystem operations.
/// </summary>
/// <remarks>
/// File paths containing Unicode characters (especially emojis and combining characters)
/// can have different binary representations that look identical to users but don't match
/// when compared at the byte level. This helper applies NFC (Canonical Composition)
/// normalization to ensure consistent path handling between SQLite storage and filesystem
/// operations.
/// </remarks>
public static class PathNormalizationHelper
{
    /// <summary>
    /// Normalizes a file path to NFC form for consistent Unicode representation.
    /// This ensures paths stored in SQLite match paths on the filesystem.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The NFC-normalized path, or empty string if input is null/empty.</returns>
    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return path ?? string.Empty;

        return path.Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Checks if a path contains characters that may have normalization issues.
    /// Useful for diagnostics when troubleshooting file-not-found errors.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <returns>True if the path has potential normalization issues (i.e., NFC normalization would change it).</returns>
    public static bool HasPotentialNormalizationIssues(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var normalized = path.Normalize(NormalizationForm.FormC);
        return !string.Equals(normalized, path, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets a diagnostic representation of a path showing UTF-8 byte values
    /// for Unicode debugging.
    /// </summary>
    /// <param name="path">The path to diagnose.</param>
    /// <returns>A string showing the UTF-8 byte representation of the path.</returns>
    public static string GetDiagnosticRepresentation(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return "(empty)";

        var bytes = Encoding.UTF8.GetBytes(path);
        return $"UTF8[{bytes.Length}]: {BitConverter.ToString(bytes)}";
    }
}
