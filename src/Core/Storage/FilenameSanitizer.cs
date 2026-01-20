using System.Globalization;
using System.Text;

namespace M365MailMirror.Core.Storage;

/// <summary>
/// Utility for sanitizing filenames and generating EML file names.
/// Handles illegal characters, path length limits, and collision avoidance.
/// </summary>
public static class FilenameSanitizer
{
    /// <summary>
    /// Characters that are illegal on any filesystem.
    /// </summary>
    private static readonly char[] IllegalChars = ['?', '*', ':', '"', '<', '>', '|', '/', '\\'];

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
    /// Calculates the maximum subject length given the current path context.
    /// Ensures the full path won't exceed OS limits.
    /// </summary>
    /// <param name="basePath">The base archive path.</param>
    /// <param name="folderPath">The mail folder path.</param>
    /// <param name="dateSubPath">The date subpath (e.g., "2024/01").</param>
    /// <param name="maxPathLength">Maximum total path length (default 260 for Windows).</param>
    /// <returns>The maximum safe subject length.</returns>
    public static int CalculateMaxSubjectLength(
        string basePath,
        string folderPath,
        string dateSubPath,
        int maxPathLength = 260)
    {
        // Calculate current path length: basePath/eml/folderPath/dateSubPath/
        var currentPathLength = basePath.Length + 1 + 3 + 1 + folderPath.Length + 1 + dateSubPath.Length + 1;

        // Reserve space for: _HHMM.eml (9 chars) + buffer (10 chars) + collision counter (_999 = 4 chars)
        const int reservedLength = 9 + 10 + 4;

        var available = maxPathLength - currentPathLength - reservedLength;

        // Ensure minimum reasonable length
        return Math.Max(10, Math.Min(available, DefaultMaxFilenameLength));
    }
}
