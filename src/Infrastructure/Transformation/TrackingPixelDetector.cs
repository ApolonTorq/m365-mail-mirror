using System.Text.RegularExpressions;

namespace M365MailMirror.Infrastructure.Transform;

/// <summary>
/// Helper class for detecting email tracking pixel images.
/// Identifies images that are used for email open tracking and analytics.
/// </summary>
public static class TrackingPixelDetector
{
    /// <summary>
    /// Timeout for regex operations to prevent catastrophic backtracking.
    /// </summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Known tracking pixel domains. Uses a HashSet for O(1) lookup.
    /// Domains are matched exactly or as parent domains (subdomain matching).
    /// </summary>
    private static readonly HashSet<string> TrackingDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        // SendGrid
        "sendgrid.net",
        "ct.sendgrid.net",
        "sendgrid.com",

        // Mailchimp / Mandrill
        "list-manage.com",
        "mailchimp.com",
        "mandrillapp.com",
        "mcdlv.net",

        // HubSpot
        "hubspotemail.net",
        "t.hubspotemail.net",

        // Sales/CRM tools
        "mixmax.com",
        "mixpanel.com",
        "yesware.com",
        "saleshandy.com",
        "salesloft.com",

        // Email tracking services
        "emltrk.com",
        "mltrk.io",
        "mailstat.us",
        "bl-1.com",
        "mailfoogae.appspot.com",

        // Cordial
        "crdl.io",
        "cordial.io",
        "track.sp.crdl.io",

        // Postmark
        "pstmrk.it",

        // Marketing automation
        "marketo.com",
        "en25.com",
        "intercom-mail.com",

        // Amazon SES
        "awstrack.me",

        // User-reported domains
        "e.p.indiegogo.com",
    };

    /// <summary>
    /// URL path patterns commonly used for tracking pixels.
    /// These patterns match the beginning of URL paths.
    /// </summary>
    private static readonly Regex TrackingPathPattern = new(
        @"^/(?:" +
            @"wf/open|" +           // SendGrid
            @"open/?|" +            // Generic open tracking
            @"o/?|" +               // Short open tracking (Indiegogo, etc.)
            @"track/?|" +           // Generic tracking
            @"trackingpixel/?|" +   // Microsoft Azure SafeLink tracking pixel
            @"trk/?|" +             // Short tracking
            @"tr/?|" +              // Very short tracking
            @"pixel/?|" +           // Pixel tracking
            @"px/?|" +              // Short pixel
            @"e2t/[oc]/?|" +        // HubSpot
            @"beacon/?|" +          // Beacon tracking
            @"img/t/?|" +           // Image tracking
            @"q/?" +                // Query/Cordial style
        @")(?:/|$|\?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>
    /// Determines whether the given image URL is a tracking pixel.
    /// </summary>
    /// <param name="url">The image URL to check.</param>
    /// <returns>True if the URL is likely a tracking pixel, false otherwise.</returns>
    public static bool IsTrackingPixel(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        // CID references and data URIs are never tracking pixels
        if (url.StartsWith("cid:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Try to parse as URI for accurate domain extraction
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false; // Not a valid absolute URL, preserve it (conservative approach)
        }

        // Only check HTTP/HTTPS URLs
        if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            // Check if domain or subdomain matches known tracking domains
            if (IsDomainTracking(uri.Host))
            {
                return true;
            }

            // Check if URL path matches tracking patterns
            if (TrackingPathPattern.IsMatch(uri.AbsolutePath))
            {
                return true;
            }

            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            // If regex times out, be conservative and don't strip
            return false;
        }
    }

    /// <summary>
    /// Checks if a hostname matches a known tracking domain.
    /// Handles both exact matches and subdomain matches.
    /// </summary>
    /// <param name="host">The hostname to check.</param>
    /// <returns>True if the host is a known tracking domain.</returns>
    private static bool IsDomainTracking(string host)
    {
        // Direct match
        if (TrackingDomains.Contains(host))
        {
            return true;
        }

        // Check if host ends with a known tracking domain (subdomain matching)
        foreach (var domain in TrackingDomains)
        {
            if (host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
