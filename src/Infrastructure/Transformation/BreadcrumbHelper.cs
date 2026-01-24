using System.Globalization;
using System.Text;

namespace M365MailMirror.Infrastructure.Transform;

/// <summary>
/// Helper class for generating breadcrumb navigation HTML and Markdown.
/// Breadcrumbs show the path hierarchy: Archive > Folder > Year > Month > [Subject]
/// </summary>
public static class BreadcrumbHelper
{
    private static readonly string[] MonthNames =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    ];

    /// <summary>
    /// Generates HTML breadcrumb navigation for an individual email file.
    /// </summary>
    /// <param name="outputPath">Relative path from archive root (e.g., "transformed/Inbox/2024/01/Meeting_1030.html")</param>
    /// <param name="subject">The email subject to display as the current item</param>
    /// <returns>HTML nav element with breadcrumb links</returns>
    public static string GenerateHtmlBreadcrumb(string outputPath, string subject)
    {
        var segments = ParseOutputPath(outputPath, isIndexFile: false);
        return BuildHtmlBreadcrumb(segments, subject);
    }

    /// <summary>
    /// Generates Markdown breadcrumb navigation for an individual email file.
    /// </summary>
    /// <param name="outputPath">Relative path from archive root (e.g., "transformed/Inbox/2024/01/Meeting_1030.md")</param>
    /// <param name="subject">The email subject to display as the current item</param>
    /// <returns>Markdown line with breadcrumb links</returns>
    public static string GenerateMarkdownBreadcrumb(string outputPath, string subject)
    {
        var segments = ParseOutputPath(outputPath, isIndexFile: false);
        return BuildMarkdownBreadcrumb(segments, subject);
    }

    /// <summary>
    /// Generates HTML breadcrumb navigation for an index file.
    /// </summary>
    /// <param name="indexPath">Relative path from archive root (e.g., "transformed/Inbox/2024/01/index.html")</param>
    /// <returns>HTML nav element with breadcrumb links</returns>
    public static string GenerateHtmlIndexBreadcrumb(string indexPath)
    {
        var segments = ParseOutputPath(indexPath, isIndexFile: true);
        return BuildHtmlBreadcrumb(segments, currentItem: null);
    }

    /// <summary>
    /// Generates Markdown breadcrumb navigation for an index file.
    /// </summary>
    /// <param name="indexPath">Relative path from archive root (e.g., "transformed/Inbox/2024/01/index.md")</param>
    /// <returns>Markdown line with breadcrumb links</returns>
    public static string GenerateMarkdownIndexBreadcrumb(string indexPath)
    {
        var segments = ParseOutputPath(indexPath, isIndexFile: true);
        return BuildMarkdownBreadcrumb(segments, currentItem: null);
    }

    /// <summary>
    /// Converts a month number (1-12) to month name.
    /// </summary>
    public static string GetMonthName(int month)
    {
        if (month < 1 || month > 12)
            return month.ToString("D2", CultureInfo.InvariantCulture);
        return MonthNames[month - 1];
    }

    /// <summary>
    /// Converts a two-digit month string to month name.
    /// </summary>
    public static string GetMonthName(string monthStr)
    {
        if (int.TryParse(monthStr, CultureInfo.InvariantCulture, out var month))
            return GetMonthName(month);
        return monthStr;
    }

    /// <summary>
    /// Parses an output path into breadcrumb segments.
    /// Path format: transformed/{folderPath}/{year}/{month}/{filename}
    /// </summary>
    private static List<BreadcrumbSegment> ParseOutputPath(string outputPath, bool isIndexFile)
    {
        var segments = new List<BreadcrumbSegment>();
        var normalizedPath = outputPath.Replace('\\', '/');
        var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 1)
            return segments;

        // Skip the output type root folder (transformed)
        var pathParts = parts.Skip(1).ToList();

        // For index files, remove the "index.html" or "index.md" filename
        if (isIndexFile && pathParts.Count > 0)
        {
            var lastPart = pathParts[^1];
            if (lastPart.Equals("index.html", StringComparison.OrdinalIgnoreCase) ||
                lastPart.Equals("index.md", StringComparison.OrdinalIgnoreCase))
            {
                pathParts.RemoveAt(pathParts.Count - 1);
            }
        }
        else if (!isIndexFile && pathParts.Count > 0)
        {
            // For email files, remove the filename
            pathParts.RemoveAt(pathParts.Count - 1);
        }

        // Calculate the depth for relative paths
        // Each segment needs to go up one level to reach root
        var depth = pathParts.Count;

        // Add "Archive" as root
        segments.Add(new BreadcrumbSegment
        {
            DisplayName = "Archive",
            RelativePath = BuildRelativePath(depth, "index"),
            IsRoot = true
        });

        // Process remaining path parts
        for (var i = 0; i < pathParts.Count; i++)
        {
            var part = pathParts[i];
            var remainingDepth = pathParts.Count - i - 1;
            var displayName = GetDisplayName(part, i, pathParts);

            segments.Add(new BreadcrumbSegment
            {
                DisplayName = displayName,
                RelativePath = BuildRelativePath(remainingDepth, "index"),
                IsLast = i == pathParts.Count - 1
            });
        }

        return segments;
    }

    /// <summary>
    /// Gets the display name for a path segment.
    /// Converts month numbers to names and handles special cases.
    /// </summary>
    private static string GetDisplayName(string part, int index, List<string> allParts)
    {
        // Check if this looks like a month (2-digit number) and previous part looks like a year
        if (part.Length == 2 && int.TryParse(part, CultureInfo.InvariantCulture, out var monthNum) && monthNum >= 1 && monthNum <= 12)
        {
            // Check if previous segment is a year
            if (index > 0 && allParts[index - 1].Length == 4 && int.TryParse(allParts[index - 1], CultureInfo.InvariantCulture, out var year) && year >= 1900 && year <= 2100)
            {
                return GetMonthName(monthNum);
            }
        }

        return part;
    }

    /// <summary>
    /// Builds a relative path with the specified depth of "../" prefixes.
    /// </summary>
    private static string BuildRelativePath(int depth, string filename)
    {
        if (depth <= 0)
            return string.Concat(filename, ".html");

        var sb = new StringBuilder();
        for (var i = 0; i < depth; i++)
        {
            sb.Append("../");
        }
        sb.Append(filename);
        sb.Append(".html");
        return sb.ToString();
    }

    /// <summary>
    /// Builds HTML breadcrumb from segments.
    /// </summary>
    private static string BuildHtmlBreadcrumb(List<BreadcrumbSegment> segments, string? currentItem)
    {
        var sb = new StringBuilder();
        sb.Append("<nav class=\"breadcrumb\">");

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var isLast = i == segments.Count - 1 && string.IsNullOrEmpty(currentItem);

            if (i > 0)
                sb.Append(" &gt; ");

            if (isLast)
            {
                sb.Append("<span class=\"current\">");
                sb.Append(System.Net.WebUtility.HtmlEncode(segment.DisplayName));
                sb.Append("</span>");
            }
            else
            {
                sb.Append("<a href=\"");
                sb.Append(segment.RelativePath);
                sb.Append("\">");
                sb.Append(System.Net.WebUtility.HtmlEncode(segment.DisplayName));
                sb.Append("</a>");
            }
        }

        // Add current item (email subject) if provided
        if (!string.IsNullOrEmpty(currentItem))
        {
            sb.Append(" &gt; ");
            sb.Append("<span class=\"current\">");
            sb.Append(System.Net.WebUtility.HtmlEncode(currentItem));
            sb.Append("</span>");
        }

        sb.Append("</nav>");
        return sb.ToString();
    }

    /// <summary>
    /// Builds Markdown breadcrumb from segments.
    /// </summary>
    private static string BuildMarkdownBreadcrumb(List<BreadcrumbSegment> segments, string? currentItem)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var isLast = i == segments.Count - 1 && string.IsNullOrEmpty(currentItem);

            if (i > 0)
                sb.Append(" > ");

            if (isLast)
            {
                sb.Append("**");
                sb.Append(segment.DisplayName);
                sb.Append("**");
            }
            else
            {
                // For markdown, use .md extension
                var mdPath = segment.RelativePath.Replace(".html", ".md", StringComparison.Ordinal);
                sb.Append('[');
                sb.Append(segment.DisplayName);
                sb.Append("](");
                sb.Append(mdPath);
                sb.Append(')');
            }
        }

        // Add current item (email subject) if provided
        if (!string.IsNullOrEmpty(currentItem))
        {
            sb.Append(" > ");
            sb.Append("**");
            sb.Append(currentItem);
            sb.Append("**");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Represents a segment in the breadcrumb trail.
    /// </summary>
    private class BreadcrumbSegment
    {
        public string DisplayName { get; init; } = "";
        public string RelativePath { get; init; } = "";
        public bool IsRoot { get; init; }
        public bool IsLast { get; init; }
    }
}
