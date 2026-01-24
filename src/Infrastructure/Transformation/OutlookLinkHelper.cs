using System.Net;

namespace M365MailMirror.Infrastructure.Transform;

/// <summary>
/// Helper class for generating Outlook Web deep links.
/// </summary>
public static class OutlookLinkHelper
{
    private const string OutlookBaseUrl = "https://outlook.office.com/mail";

    /// <summary>
    /// Generates an Outlook Web deep link URL for viewing a message.
    /// </summary>
    /// <param name="immutableId">The Microsoft Graph ImmutableId of the message.</param>
    /// <param name="mailbox">Optional mailbox email for shared mailbox scenarios.</param>
    /// <returns>The full Outlook Web URL, or null if immutableId is null/empty.</returns>
    public static string? GenerateOutlookUrl(string? immutableId, string? mailbox)
    {
        if (string.IsNullOrEmpty(immutableId))
            return null;

        // URL-encode the ImmutableId (it may contain special characters like + and /)
        var encodedId = WebUtility.UrlEncode(immutableId);

        // Shared mailbox format: /mail/{mailbox}/deeplink/read/{id}
        // Personal mailbox format: /mail/deeplink/read/{id}
        if (!string.IsNullOrEmpty(mailbox))
        {
            return $"{OutlookBaseUrl}/{WebUtility.UrlEncode(mailbox)}/deeplink/read/{encodedId}";
        }

        return $"{OutlookBaseUrl}/deeplink/read/{encodedId}";
    }

    /// <summary>
    /// Generates HTML for the "View in Outlook" link.
    /// </summary>
    /// <param name="immutableId">The Microsoft Graph ImmutableId of the message.</param>
    /// <param name="mailbox">Optional mailbox email for shared mailbox scenarios.</param>
    /// <returns>HTML string with anchor tag, or empty string if no URL available.</returns>
    public static string GenerateHtmlLink(string? immutableId, string? mailbox)
    {
        var url = GenerateOutlookUrl(immutableId, mailbox);
        if (url == null)
            return "";

        return $"            <div><a href=\"{WebUtility.HtmlEncode(url)}\" target=\"_blank\" rel=\"noopener noreferrer\">View in Outlook</a></div>\n";
    }

    /// <summary>
    /// Generates Markdown YAML front matter for the Outlook URL.
    /// </summary>
    /// <param name="immutableId">The Microsoft Graph ImmutableId of the message.</param>
    /// <param name="mailbox">Optional mailbox email for shared mailbox scenarios.</param>
    /// <returns>YAML front matter line, or empty string if no URL available.</returns>
    public static string GenerateMarkdownFrontMatter(string? immutableId, string? mailbox)
    {
        var url = GenerateOutlookUrl(immutableId, mailbox);
        if (url == null)
            return "";

        return $"outlookUrl: \"{url}\"\n";
    }

    /// <summary>
    /// Generates Markdown display line for the "View in Outlook" link.
    /// </summary>
    /// <param name="immutableId">The Microsoft Graph ImmutableId of the message.</param>
    /// <param name="mailbox">Optional mailbox email for shared mailbox scenarios.</param>
    /// <returns>Markdown link line, or empty string if no URL available.</returns>
    public static string GenerateMarkdownDisplayLine(string? immutableId, string? mailbox)
    {
        var url = GenerateOutlookUrl(immutableId, mailbox);
        if (url == null)
            return "";

        return $"[View in Outlook]({url})\n";
    }
}
