using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace M365MailMirror.Core.Storage;

/// <summary>
/// Utility for sanitizing filenames and generating EML file names.
/// Handles illegal characters, path length limits, and collision avoidance.
/// </summary>
public static partial class FilenameSanitizer
{
    /// <summary>
    /// Characters that are illegal on any filesystem.
    /// </summary>
    private static readonly char[] IllegalChars = ['?', '*', ':', '"', '<', '>', '|', '/', '\\'];

    /// <summary>
    /// Regex to match two or more consecutive dashes.
    /// </summary>
    [GeneratedRegex(@"-{2,}")]
    private static partial Regex ConsecutiveDashesRegex();

    /// <summary>
    /// Maximum filename length to prevent path limit issues.
    /// Windows MAX_PATH is 260, leaving room for path prefix and extension.
    /// </summary>
    private const int DefaultMaxFilenameLength = 100;

    /// <summary>
    /// Generates a sanitized filename for an EML file.
    /// Format: {sanitized-subject}_{HHMM}.eml
    /// </summary>
    /// <param name="subject">The message subject.</param>
    /// <param name="receivedTime">When the message was received.</param>
    /// <param name="maxLength">Maximum length for the subject portion (default 100).</param>
    /// <returns>The sanitized filename including .eml extension.</returns>
    public static string GenerateEmlFilename(string? subject, DateTimeOffset receivedTime, int maxLength = DefaultMaxFilenameLength)
    {
        var sanitizedSubject = SanitizeFilename(subject ?? "No Subject", maxLength);
        var timeSuffix = receivedTime.ToString("HHmm", CultureInfo.InvariantCulture);
        return $"{sanitizedSubject}_{timeSuffix}.eml";
    }

    /// <summary>
    /// Generates a filename with collision counter.
    /// Format: {sanitized-subject}_{HHMM}_{counter}.eml
    /// </summary>
    /// <param name="subject">The message subject.</param>
    /// <param name="receivedTime">When the message was received.</param>
    /// <param name="collisionCounter">Counter to avoid collisions (1, 2, 3, etc.).</param>
    /// <param name="maxLength">Maximum length for the subject portion.</param>
    /// <returns>The sanitized filename with collision counter.</returns>
    public static string GenerateEmlFilenameWithCounter(
        string? subject,
        DateTimeOffset receivedTime,
        int collisionCounter,
        int maxLength = DefaultMaxFilenameLength)
    {
        var sanitizedSubject = SanitizeFilename(subject ?? "No Subject", maxLength);
        var timeSuffix = receivedTime.ToString("HHmm", CultureInfo.InvariantCulture);
        return $"{sanitizedSubject}_{timeSuffix}_{collisionCounter}.eml";
    }

    /// <summary>
    /// Sanitizes a filename by removing or replacing illegal characters.
    /// </summary>
    /// <param name="filename">The filename to sanitize.</param>
    /// <param name="maxLength">Maximum length for the result.</param>
    /// <returns>The sanitized filename.</returns>
    public static string SanitizeFilename(string filename, int maxLength = DefaultMaxFilenameLength)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return "unnamed";
        }

        // Normalize to NFC for consistency across platforms
        var normalized = filename.Normalize(NormalizationForm.FormC);

        // Build sanitized string
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (IllegalChars.Contains(ch))
            {
                sb.Append('_');
            }
            else if (char.IsControl(ch))
            {
                // Skip control characters
                continue;
            }
            else
            {
                sb.Append(ch);
            }
        }

        var result = sb.ToString();

        // Trim trailing dots and spaces (Windows requirement)
        result = result.TrimEnd('.', ' ');

        // Trim leading dots and spaces for consistency
        result = result.TrimStart('.', ' ');

        // Ensure not empty after sanitization
        if (string.IsNullOrWhiteSpace(result))
        {
            return "unnamed";
        }

        // Truncate if too long
        if (result.Length > maxLength)
        {
            result = result[..maxLength].TrimEnd('.', ' ');
        }

        // Ensure not empty after truncation
        if (string.IsNullOrWhiteSpace(result))
        {
            return "unnamed";
        }

        return result;
    }

    /// <summary>
    /// Sanitizes a folder path, handling each component separately.
    /// </summary>
    /// <param name="folderPath">The folder path (e.g., "Inbox/Important").</param>
    /// <returns>The sanitized folder path.</returns>
    public static string SanitizeFolderPath(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return "Unknown";
        }

        // Split by forward slash (Graph API uses forward slashes)
        var parts = folderPath.Split('/');
        var sanitizedParts = parts
            .Select(p => SanitizeFilename(p, 50))
            .Where(p => !string.IsNullOrEmpty(p));

        return string.Join(Path.DirectorySeparatorChar.ToString(), sanitizedParts);
    }

    /// <summary>
    /// Generates a sanitized folder prefix for filenames.
    /// Nested folders are joined with '-' (e.g., "Inbox/Processed" becomes "inbox-processed").
    /// Spaces and illegal characters are replaced with '-'.
    /// When the result would exceed maxLength, middle folder levels are dropped while
    /// preserving the root (first) and deepest (last) folder names.
    /// </summary>
    /// <param name="folderPath">The M365 folder path (e.g., "Inbox/Processed").</param>
    /// <param name="maxLength">Maximum length for the folder prefix (default 30).</param>
    /// <returns>The sanitized folder prefix in lowercase.</returns>
    public static string GenerateFolderPrefix(string? folderPath, int maxLength = 30)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return "unknown";
        }

        // Split by forward slash (Graph API uses forward slashes)
        var parts = folderPath.Split('/');
        var sanitizedParts = parts
            .Select(p => SanitizeFilenameForPrefix(p, 50))
            .Where(p => !string.IsNullOrEmpty(p) && p != "unnamed")
            .ToList();

        if (sanitizedParts.Count == 0)
        {
            return "unknown";
        }

        // Try joining all parts first
        var result = string.Join("-", sanitizedParts);

        if (result.Length <= maxLength)
        {
            return result;
        }

        // Smart truncation: keep root (first) and deepest (last) folders, drop middle ones
        if (sanitizedParts.Count >= 2)
        {
            var first = sanitizedParts[0];
            var last = sanitizedParts[^1];

            // Try keeping just first and last
            result = $"{first}-{last}";

            if (result.Length <= maxLength)
            {
                return result;
            }

            // Even first+last is too long - truncate the last folder name
            // Reserve space for first folder + dash
            var reservedForFirst = first.Length + 1; // "inbox-"
            var availableForLast = maxLength - reservedForFirst;

            if (availableForLast > 0)
            {
                var truncatedLast = last.Length > availableForLast
                    ? last[..availableForLast].TrimEnd('-', '.', ' ')
                    : last;

                if (!string.IsNullOrEmpty(truncatedLast))
                {
                    return $"{first}-{truncatedLast}";
                }
            }

            // Fall back to just first folder, truncated if needed
            return first.Length > maxLength
                ? first[..maxLength].TrimEnd('-', '.', ' ')
                : first;
        }

        // Single folder - just truncate if needed
        if (result.Length > maxLength)
        {
            result = result[..maxLength].TrimEnd('-', '.', ' ');
        }

        return string.IsNullOrEmpty(result) ? "unknown" : result;
    }

    /// <summary>
    /// Sanitizes a filename for use in prefixed filenames.
    /// Unlike <see cref="SanitizeFilename"/>, this replaces spaces and illegal characters with '-'
    /// (preserving '_' as the component separator) and converts to lowercase.
    /// Consecutive dashes are collapsed to a single dash.
    /// </summary>
    /// <param name="filename">The filename to sanitize.</param>
    /// <param name="maxLength">Maximum length for the result.</param>
    /// <returns>The sanitized filename in lowercase with dashes.</returns>
    public static string SanitizeFilenameForPrefix(string? filename, int maxLength = DefaultMaxFilenameLength)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return "unnamed";
        }

        // Normalize to NFC for consistency across platforms
        var normalized = filename.Normalize(NormalizationForm.FormC);

        // Build sanitized string
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (IllegalChars.Contains(ch) || ch == ' ')
            {
                sb.Append('-');
            }
            else if (char.IsControl(ch))
            {
                // Skip control characters
                continue;
            }
            else
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        var result = sb.ToString();

        // Collapse consecutive dashes to single dash (e.g., "---" -> "-")
        result = ConsecutiveDashesRegex().Replace(result, "-");

        // Trim trailing dashes and spaces
        result = result.TrimEnd('-', '.', ' ');

        // Trim leading dashes and spaces
        result = result.TrimStart('-', '.', ' ');

        // Ensure not empty after sanitization
        if (string.IsNullOrWhiteSpace(result))
        {
            return "unnamed";
        }

        // Truncate if too long
        if (result.Length > maxLength)
        {
            result = result[..maxLength].TrimEnd('-', '.', ' ');
        }

        // Ensure not empty after truncation
        if (string.IsNullOrWhiteSpace(result))
        {
            return "unnamed";
        }

        return result;
    }

    /// <summary>
    /// Generates a sanitized filename for an EML file with folder and datetime prefixes.
    /// Format: {folder-prefix}_{YYYY-MM-DD-HH-MM-SS}_{sanitized-subject}.eml
    /// </summary>
    /// <param name="folderPath">The M365 folder path.</param>
    /// <param name="subject">The message subject.</param>
    /// <param name="receivedTime">When the message was received.</param>
    /// <param name="maxSubjectLength">Maximum length for the subject portion (default 50).</param>
    /// <returns>The sanitized filename including .eml extension.</returns>
    public static string GenerateEmlFilenameWithPrefixes(
        string? folderPath,
        string? subject,
        DateTimeOffset receivedTime,
        int maxSubjectLength = 50)
    {
        var folderPrefix = GenerateFolderPrefix(folderPath);
        var datetime = receivedTime.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture);
        var sanitizedSubject = SanitizeFilenameForPrefix(subject ?? "No Subject", maxSubjectLength);

        return $"{folderPrefix}_{datetime}_{sanitizedSubject}.eml";
    }

    /// <summary>
    /// Generates a filename with folder/datetime prefixes and collision counter.
    /// Format: {folder-prefix}_{YYYY-MM-DD-HH-MM-SS}_{sanitized-subject}_{counter}.eml
    /// </summary>
    /// <param name="folderPath">The M365 folder path.</param>
    /// <param name="subject">The message subject.</param>
    /// <param name="receivedTime">When the message was received.</param>
    /// <param name="collisionCounter">Counter to avoid collisions (1, 2, 3, etc.).</param>
    /// <param name="maxSubjectLength">Maximum length for the subject portion.</param>
    /// <returns>The sanitized filename with collision counter.</returns>
    public static string GenerateEmlFilenameWithPrefixesAndCounter(
        string? folderPath,
        string? subject,
        DateTimeOffset receivedTime,
        int collisionCounter,
        int maxSubjectLength = 50)
    {
        var folderPrefix = GenerateFolderPrefix(folderPath);
        var datetime = receivedTime.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture);
        var sanitizedSubject = SanitizeFilenameForPrefix(subject ?? "No Subject", maxSubjectLength);

        return $"{folderPrefix}_{datetime}_{sanitizedSubject}_{collisionCounter}.eml";
    }

    /// <summary>
    /// Calculates the maximum subject length given the current path context.
    /// Ensures the full path won't exceed OS limits.
    /// </summary>
    /// <param name="basePath">The base archive path.</param>
    /// <param name="dateSubPath">The date subpath (e.g., "2024/01").</param>
    /// <param name="maxPathLength">Maximum total path length (default 260 for Windows).</param>
    /// <returns>The maximum safe subject length.</returns>
    public static int CalculateMaxSubjectLength(
        string basePath,
        string dateSubPath,
        int maxPathLength = 260)
    {
        // Calculate current path length: basePath/eml/dateSubPath/
        var currentPathLength = basePath.Length + 1 + 3 + 1 + dateSubPath.Length + 1;

        // Reserve space for prefixed filename format:
        // {folder-prefix}_ (31 chars max: 30 + underscore)
        // {YYYY-MM-DD-HH-MM-SS}_ (20 chars: 19 + underscore)
        // .eml (4 chars)
        // buffer (10 chars)
        // collision counter _999 (4 chars)
        const int reservedLength = 31 + 20 + 4 + 10 + 4; // = 69

        var available = maxPathLength - currentPathLength - reservedLength;

        // Ensure minimum reasonable length, cap at 50 for prefixed format
        return Math.Max(10, Math.Min(available, 50));
    }
}
