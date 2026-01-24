using System.Text.RegularExpressions;

using M365MailMirror.Core.Graph;

namespace M365MailMirror.Core.Sync;

/// <summary>
/// Matches folder paths against glob patterns.
/// Supports wildcards: * (single segment), ** (recursive).
/// </summary>
/// <remarks>
/// Pattern examples:
/// <list type="bullet">
///   <item><c>Inbox</c> - Matches "Inbox" and all descendants</item>
///   <item><c>Inbox/Azure*</c> - Matches folders starting with "Azure" under Inbox</item>
///   <item><c>Robots/*</c> - Matches immediate children of Robots only</item>
///   <item><c>Robots/**</c> - Matches all descendants of Robots (not Robots itself)</item>
///   <item><c>**/Old*</c> - Matches any folder starting with "Old" at any depth</item>
/// </list>
/// All matching is case-insensitive.
/// </remarks>
public class FolderGlobMatcher
{
    private readonly List<CompiledPattern> _patterns;

    /// <summary>
    /// Creates a matcher from a list of exclusion patterns.
    /// </summary>
    /// <param name="patterns">Glob patterns to match against.</param>
    public FolderGlobMatcher(IEnumerable<string> patterns)
    {
        _patterns = patterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(CompilePattern)
            .ToList();
    }

    /// <summary>
    /// Checks if a folder path matches any of the exclusion patterns.
    /// </summary>
    /// <param name="folderPath">The folder path to check (e.g., "Inbox/Important").</param>
    /// <returns>True if the path should be excluded.</returns>
    public bool IsMatch(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath))
        {
            return false;
        }

        return _patterns.Any(p => p.IsMatch(folderPath));
    }

    /// <summary>
    /// Filters a list of folders, returning only those NOT matching any pattern.
    /// </summary>
    /// <param name="folders">The folders to filter.</param>
    /// <returns>Folders that don't match any exclusion pattern.</returns>
    public IReadOnlyList<AppMailFolder> FilterFolders(IReadOnlyList<AppMailFolder> folders)
    {
        return folders
            .Where(f => !IsMatch(f.FullPath))
            .ToList();
    }

    private static CompiledPattern CompilePattern(string pattern)
    {
        // Determine if this is a simple pattern (no wildcards)
        bool hasWildcard = pattern.Contains('*');

        if (!hasWildcard)
        {
            // Simple pattern: match exact path and all descendants
            // "Inbox" matches "Inbox", "Inbox/Work", "Inbox/Work/Projects"
            var escaped = Regex.Escape(pattern);
            var regex = new Regex($"^{escaped}(/.*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            return new CompiledPattern(regex);
        }

        // Pattern has wildcards - convert to regex
        var regexPattern = ConvertGlobToRegex(pattern);
        var compiledRegex = new Regex($"^{regexPattern}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        return new CompiledPattern(compiledRegex);
    }

    private static string ConvertGlobToRegex(string pattern)
    {
        // Handle special case: pattern ends with /** (all descendants, not parent)
        // "Robots/**" → matches "Robots/Bot1", "Robots/Bot1/Logs" but NOT "Robots"
        if (pattern.EndsWith("/**", StringComparison.Ordinal))
        {
            var prefix = pattern[..^3]; // Remove /**
            var escapedPrefix = Regex.Escape(prefix);
            return $"{escapedPrefix}/.+";
        }

        // Handle special case: pattern ends with /* (immediate children only)
        // "Robots/*" → matches "Robots/Bot1" but NOT "Robots" or "Robots/Bot1/Logs"
        if (pattern.EndsWith("/*", StringComparison.Ordinal))
        {
            var prefix = pattern[..^2]; // Remove /*
            var escapedPrefix = Regex.Escape(prefix);
            return $"{escapedPrefix}/[^/]+";
        }

        // Handle special case: pattern starts with **/ (match at any level)
        // "**/Old*" → matches "Old", "OldStuff", "Inbox/Old", "Archive/2024/OldMessages"
        if (pattern.StartsWith("**/", StringComparison.Ordinal))
        {
            var suffix = pattern[3..]; // Remove **/
            var suffixRegex = ConvertSegmentPattern(suffix);
            // Match at root level or after any path prefix
            return $"(.*/)?" + suffixRegex;
        }

        // General case: convert each segment
        var segments = pattern.Split('/');
        var regexSegments = segments.Select(ConvertSegmentPattern);
        return string.Join("/", regexSegments);
    }

    private static string ConvertSegmentPattern(string segment)
    {
        if (segment == "**")
        {
            // ** matches any number of path segments (including zero)
            return ".*";
        }

        if (segment == "*")
        {
            // * matches exactly one path segment
            return "[^/]+";
        }

        // Escape special regex characters, then convert * to regex
        var escaped = Regex.Escape(segment);
        // Replace escaped \* with [^/]* (match any chars except /)
        return escaped.Replace(@"\*", "[^/]*");
    }

    private sealed class CompiledPattern(Regex regex)
    {
        public bool IsMatch(string path) => regex.IsMatch(path);
    }
}
