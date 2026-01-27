using System.Text.RegularExpressions;
using M365MailMirror.Core.Database.Entities;

namespace M365MailMirror.Infrastructure.Transform;

/// <summary>
/// Helper class for rewriting Content-ID (cid:) references in HTML and Markdown content.
/// Handles mapping cid: references to extracted attachment file paths.
/// </summary>
public static class CidRewriteHelper
{
    /// <summary>
    /// Timeout for regex operations to prevent catastrophic backtracking.
    /// </summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Rewrites cid: references in HTML to point to extracted images.
    /// </summary>
    /// <param name="html">The HTML body content</param>
    /// <param name="outputPath">The output path of the HTML file (for calculating relative paths)</param>
    /// <param name="attachments">The list of attachments including those with ContentId</param>
    /// <returns>HTML with cid: references replaced with relative paths to images</returns>
    public static string RewriteCidReferencesHtml(string html, string outputPath, IReadOnlyList<Attachment> attachments)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        var cidToPath = BuildCidToPathMapping(outputPath, attachments);

        if (cidToPath.Count == 0)
            return html;

        // Replace cid: references with relative paths
        // Matches: src="cid:xxx" or src='cid:xxx'
        return Regex.Replace(
            html,
            @"(src\s*=\s*[""'])cid:([^""']+)([""'])",
            match => ReplaceCidMatch(match, cidToPath, isMarkdown: false),
            RegexOptions.IgnoreCase,
            RegexTimeout);
    }

    /// <summary>
    /// Rewrites cid: references in Markdown to point to extracted images.
    /// Handles the ![image](cid:xxx) format produced by ConvertHtmlToMarkdown.
    /// </summary>
    /// <param name="markdown">The markdown body content</param>
    /// <param name="outputPath">The output path of the markdown file (for calculating relative paths)</param>
    /// <param name="attachments">The list of attachments including those with ContentId</param>
    /// <returns>Markdown with cid: references replaced with relative paths to images</returns>
    public static string RewriteCidReferencesMarkdown(string markdown, string outputPath, IReadOnlyList<Attachment> attachments)
    {
        if (string.IsNullOrEmpty(markdown))
            return markdown;

        var cidToPath = BuildCidToPathMapping(outputPath, attachments);

        if (cidToPath.Count == 0)
            return markdown;

        // Replace cid: references in markdown image syntax: ![alt](cid:xxx)
        return Regex.Replace(
            markdown,
            @"!\[([^\]]*)\]\(cid:([^)]+)\)",
            match =>
            {
                var altText = match.Groups[1].Value;
                var cidValue = match.Groups[2].Value;

                var relativePath = LookupCid(cidValue, cidToPath);
                if (relativePath != null)
                {
                    return $"![{altText}]({relativePath})";
                }

                // If not found, leave the original reference (will be cleaned by CleanCidReferences)
                return match.Value;
            },
            RegexOptions.None,
            RegexTimeout);
    }

    /// <summary>
    /// Builds a mapping from Content-ID values to relative file paths.
    /// Considers ALL attachments with a ContentId, regardless of IsInline flag,
    /// because some email clients mark referenced images as Content-Disposition: attachment
    /// while still using cid: references in the HTML body.
    /// </summary>
    /// <param name="outputPath">The output path of the file being generated</param>
    /// <param name="attachments">The list of attachments</param>
    /// <returns>Dictionary mapping ContentId to relative file path</returns>
    internal static Dictionary<string, string> BuildCidToPathMapping(string outputPath, IReadOnlyList<Attachment> attachments)
    {
        var cidToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attachment in attachments)
        {
            // Include ANY attachment with a ContentId, not just those marked as inline.
            // Some email clients (like Outlook) mark images as Content-Disposition: attachment
            // but still reference them via cid: in the HTML body.
            if (!string.IsNullOrEmpty(attachment.ContentId) && !attachment.Skipped && attachment.FilePath != null)
            {
                var relativePath = CalculateRelativePathToAttachment(outputPath, attachment.FilePath);
                cidToPath[attachment.ContentId] = relativePath;
            }
        }

        return cidToPath;
    }

    /// <summary>
    /// Looks up a CID value in the mapping, handling angle bracket variations.
    /// </summary>
    /// <param name="cidValue">The CID value from the content</param>
    /// <param name="cidToPath">The CID to path mapping</param>
    /// <returns>The relative path if found, null otherwise</returns>
    internal static string? LookupCid(string cidValue, Dictionary<string, string> cidToPath)
    {
        var lookupCid = cidValue.Trim();

        if (cidToPath.TryGetValue(lookupCid, out var relativePath))
        {
            return relativePath;
        }

        // Also try without angle brackets if the cid has them
        if (lookupCid.StartsWith('<') && lookupCid.EndsWith('>'))
        {
            lookupCid = lookupCid[1..^1];
            if (cidToPath.TryGetValue(lookupCid, out relativePath))
            {
                return relativePath;
            }
        }

        return null;
    }

    /// <summary>
    /// Calculates the relative path from an output file to an attachment.
    /// </summary>
    /// <param name="outputFilePath">Relative path to output file from archive root</param>
    /// <param name="attachmentFilePath">Relative path to attachment from archive root</param>
    /// <returns>Relative path with forward slashes for HTML/Markdown compatibility</returns>
    internal static string CalculateRelativePathToAttachment(string outputFilePath, string attachmentFilePath)
    {
        // Get directory containing the output file
        var outputDir = Path.GetDirectoryName(outputFilePath);
        if (string.IsNullOrEmpty(outputDir))
        {
            return attachmentFilePath.Replace(Path.DirectorySeparatorChar, '/');
        }

        // Normalize separators for consistent splitting
        var normalizedOutputDir = outputDir.Replace(Path.DirectorySeparatorChar, '/');
        var normalizedAttachmentPath = attachmentFilePath.Replace(Path.DirectorySeparatorChar, '/');

        // Split both paths into components
        var outputParts = normalizedOutputDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var attachmentParts = normalizedAttachmentPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Find common prefix length
        var commonLength = 0;
        var minLength = Math.Min(outputParts.Length, attachmentParts.Length);
        for (var i = 0; i < minLength; i++)
        {
            if (outputParts[i].Equals(attachmentParts[i], StringComparison.OrdinalIgnoreCase))
                commonLength++;
            else
                break;
        }

        // Build relative path: go up from output dir, then down to attachment
        var upCount = outputParts.Length - commonLength;
        var upParts = Enumerable.Repeat("..", upCount);
        var downParts = attachmentParts.Skip(commonLength);

        return string.Join("/", upParts.Concat(downParts));
    }

    private static string ReplaceCidMatch(Match match, Dictionary<string, string> cidToPath, bool isMarkdown)
    {
        var prefix = match.Groups[1].Value;
        var cidValue = match.Groups[2].Value;
        var suffix = match.Groups[3].Value;

        var relativePath = LookupCid(cidValue, cidToPath);
        if (relativePath != null)
        {
            return $"{prefix}{relativePath}{suffix}";
        }

        // If not found, leave the original reference
        return match.Value;
    }
}
