using System.Text.RegularExpressions;

namespace M365MailMirror.Infrastructure.Transform;

/// <summary>
/// Helper class for cleaning text content during EML to Markdown transformation.
/// Handles common artifacts from email formatting that don't translate well to Markdown.
/// </summary>
public static class MarkdownCleaningHelper
{
    /// <summary>
    /// Maximum number of iterations for HTML stripping loop to prevent
    /// CPU-intensive processing on pathological input.
    /// </summary>
    public const int MaxStripIterations = 100;

    /// <summary>
    /// Maximum content length to process through regex-based cleaning.
    /// Content larger than this will be truncated to prevent excessive CPU usage.
    /// 1MB is generous for typical email text while protecting against embedded data URIs
    /// with large base64 images (like mxGraph diagrams with embedded JPEGs).
    /// </summary>
    public const int MaxContentLength = 1 * 1024 * 1024; // 1 MB

    /// <summary>
    /// Timeout for individual regex operations to prevent catastrophic backtracking.
    /// </summary>
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    // Pre-compiled regex patterns with timeout for better performance and safety
    private static readonly Regex CidBracketedPattern = new(
        @"\[cid:[^\]]+\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex CidUnbracketedPattern = new(
        @"cid:[^\s\[\]<>]+@[^\s\[\]<>]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex HtmlTagPattern = new(
        @"<[^>]+>",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex ExcessiveNewlinesPattern = new(
        @"\n{3,}",
        RegexOptions.Compiled,
        RegexTimeout);

    // Outlook link patterns - use [^\s<]+ (non-whitespace, non-<) instead of \S+?
    // to prevent catastrophic backtracking while still matching full link text
    private static readonly Regex OutlookHttpLinkPattern = new(
        @"([^\s<]+)<(https?://[^>]+)>",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex OutlookMailtoLinkPattern = new(
        @"([^\s<]+)<(mailto:[^>]+)>",
        RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>
    /// Removes Content-ID (cid:) references from text that couldn't be resolved to actual images.
    /// These typically appear as [cid:image001.gif@01CA8DDC.A40BF8D0] in email bodies when
    /// inline images are referenced but not properly embedded.
    /// </summary>
    /// <param name="text">The text content to clean</param>
    /// <returns>Text with CID references removed</returns>
    public static string CleanCidReferences(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Apply content length limit
        var workingText = TruncateIfNeeded(text);

        try
        {
            // Remove patterns like [cid:image001.gif@01CA8DDC.A40BF8D0]
            workingText = CidBracketedPattern.Replace(workingText, "");

            // Remove standalone cid:xxx@xxx references (with bounded character classes to prevent backtracking)
            workingText = CidUnbracketedPattern.Replace(workingText, "");

            return workingText;
        }
        catch (RegexMatchTimeoutException)
        {
            // If regex times out, return the original text rather than fail
            return text.Length > MaxContentLength
                ? text[..MaxContentLength] + "\n\n[Content truncated - regex processing timed out]"
                : text;
        }
    }

    /// <summary>
    /// Converts Outlook-style inline links (text&lt;url&gt;) to proper Markdown links [text](url).
    /// These occur when HTML anchor tags are stripped but the URL in angle brackets remains,
    /// a common pattern in plain-text representations of Outlook/Exchange emails.
    /// </summary>
    /// <param name="text">The text content to clean</param>
    /// <returns>Text with Outlook-style links converted to Markdown format</returns>
    public static string CleanOutlookStyleLinks(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Apply content length limit
        var workingText = TruncateIfNeeded(text);

        try
        {
            // Convert patterns like "Click<http://example.com>" to "[Click](http://example.com)"
            // Using \w+ instead of \S+? to avoid catastrophic backtracking
            workingText = OutlookHttpLinkPattern.Replace(workingText, "[$1]($2)");
            workingText = OutlookMailtoLinkPattern.Replace(workingText, "[$1]($2)");

            return workingText;
        }
        catch (RegexMatchTimeoutException)
        {
            // If regex times out, return the truncated text rather than fail
            return workingText;
        }
    }

    /// <summary>
    /// Strips HTML tags from content and decodes HTML entities.
    /// Uses multi-pass stripping to handle nested tags.
    /// Includes safety limits to prevent CPU-intensive processing on pathological input.
    /// </summary>
    /// <param name="html">The HTML content to strip</param>
    /// <returns>Plain text with HTML removed and entities decoded</returns>
    public static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        // Safety limit: truncate extremely large content to prevent excessive CPU usage
        var workingContent = TruncateIfNeeded(html);

        try
        {
            // Multi-pass HTML stripping to handle nested tags
            // Limit iterations to prevent pathological regex behavior
            string result = workingContent;
            string previous;
            int iterations = 0;
            do
            {
                previous = result;
                result = HtmlTagPattern.Replace(result, "");
                iterations++;
            } while (result != previous && iterations < MaxStripIterations);

            // Decode HTML entities
            result = System.Net.WebUtility.HtmlDecode(result);

            // Normalize excessive whitespace (more than 2 consecutive newlines)
            result = ExcessiveNewlinesPattern.Replace(result, "\n\n");

            return result.Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            // If regex times out, return decoded content without full HTML stripping
            return System.Net.WebUtility.HtmlDecode(workingContent).Trim();
        }
    }

    /// <summary>
    /// Truncates content if it exceeds the maximum length, appending a truncation notice.
    /// </summary>
    private static string TruncateIfNeeded(string content)
    {
        if (content.Length > MaxContentLength)
        {
            return content[..MaxContentLength] + "\n\n[Content truncated - exceeded maximum processing length]";
        }
        return content;
    }

    /// <summary>
    /// Decodes HTML entities in text content.
    /// Useful for header fields that may contain encoded characters from Graph API.
    /// </summary>
    /// <param name="text">The text to decode</param>
    /// <returns>Text with HTML entities decoded</returns>
    public static string DecodeHtmlEntities(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return System.Net.WebUtility.HtmlDecode(text);
    }

    /// <summary>
    /// Applies the full text cleaning pipeline: strips HTML, removes CID references,
    /// and converts Outlook-style links to Markdown format.
    /// </summary>
    /// <param name="text">The text content to clean</param>
    /// <param name="isHtml">Whether the input is HTML (will be stripped) or plain text</param>
    /// <returns>Cleaned text suitable for Markdown output</returns>
    public static string CleanTextForMarkdown(string text, bool isHtml = false)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (isHtml)
        {
            text = StripHtml(text);
        }

        text = CleanCidReferences(text);
        text = CleanOutlookStyleLinks(text);

        return text;
    }
}
