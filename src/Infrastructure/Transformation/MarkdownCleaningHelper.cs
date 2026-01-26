using System.Text.RegularExpressions;

namespace M365MailMirror.Infrastructure.Transform;

/// <summary>
/// Helper class for cleaning text content during EML to Markdown transformation.
/// Handles common artifacts from email formatting that don't translate well to Markdown.
/// </summary>
public static class MarkdownCleaningHelper
{
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

        // Remove patterns like [cid:image001.gif@01CA8DDC.A40BF8D0] or standalone cid:xxx@xxx
        // Handles both bracketed [cid:...] and unbracketed cid:... references
        return Regex.Replace(
            text,
            @"\[cid:[^\]]+\]|cid:\S+@\S+",
            "",
            RegexOptions.IgnoreCase);
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

        // Convert patterns like "Click here<http://example.com>" to "[Click here](http://example.com)"
        // Also handles mailto: links
        text = Regex.Replace(
            text,
            @"(\S+?)<(https?://[^>]+)>",
            "[$1]($2)");

        text = Regex.Replace(
            text,
            @"(\S+?)<(mailto:[^>]+)>",
            "[$1]($2)");

        return text;
    }

    /// <summary>
    /// Strips HTML tags from content and decodes HTML entities.
    /// Uses multi-pass stripping to handle nested tags.
    /// </summary>
    /// <param name="html">The HTML content to strip</param>
    /// <returns>Plain text with HTML removed and entities decoded</returns>
    public static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        // Multi-pass HTML stripping to handle nested tags
        string result = html;
        string previous;
        do
        {
            previous = result;
            result = Regex.Replace(result, "<[^>]+>", "");
        } while (result != previous);

        // Decode HTML entities
        result = System.Net.WebUtility.HtmlDecode(result);

        // Normalize excessive whitespace (more than 2 consecutive newlines)
        result = Regex.Replace(result, @"\n{3,}", "\n\n");

        return result.Trim();
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
