using M365MailMirror.Infrastructure.Transform;

namespace M365MailMirror.UnitTests.Transform;

/// <summary>
/// Unit tests for TrackingPixelDetector which identifies email tracking pixel URLs.
/// </summary>
public class TrackingPixelDetectorTests
{
    #region Domain-Based Detection Tests

    [Theory]
    [InlineData("https://ct.sendgrid.net/wf/open?u=xxxx", true)]
    [InlineData("https://sendgrid.net/tracking/pixel.gif", true)]
    [InlineData("http://track.sp.crdl.io/q/xyz123", true)]
    [InlineData("https://crdl.io/pixel", true)]
    [InlineData("https://t.hubspotemail.net/e2t/o/xxxx", true)]
    [InlineData("https://emltrk.com/track", true)]
    [InlineData("https://mltrk.io/open", true)]
    [InlineData("https://list-manage.com/track/open.php", true)]
    [InlineData("https://mailchimp.com/pixel", true)]
    [InlineData("https://mandrillapp.com/track", true)]
    [InlineData("https://pstmrk.it/open", true)]
    [InlineData("https://marketo.com/track", true)]
    [InlineData("https://mixmax.com/api/track", true)]
    [InlineData("https://yesware.com/track", true)]
    [InlineData("https://saleshandy.com/pixel", true)]
    [InlineData("https://mailstat.us/tr/t/xxxx", true)]
    [InlineData("https://awstrack.me/open", true)]
    [InlineData("https://intercom-mail.com/track", true)]
    [InlineData("https://salesloft.com/pixel", true)]
    [InlineData("https://mailfoogae.appspot.com/track", true)]
    [InlineData("https://bl-1.com/pixel", true)]
    public void IsTrackingPixel_WithKnownTrackingDomain_ReturnsTrue(string url, bool expected)
    {
        var result = TrackingPixelDetector.IsTrackingPixel(url);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://sub.sendgrid.net/pixel", true)]
    [InlineData("https://tracking.crdl.io/open", true)]
    [InlineData("https://email.mailchimp.com/pixel", true)]
    public void IsTrackingPixel_WithSubdomainOfTrackingDomain_ReturnsTrue(string url, bool expected)
    {
        var result = TrackingPixelDetector.IsTrackingPixel(url);

        result.Should().Be(expected);
    }

    #endregion

    #region Path-Based Detection Tests

    [Theory]
    [InlineData("https://unknown-domain.com/wf/open?id=123", true)]
    [InlineData("https://example.com/open/track", true)]
    [InlineData("https://example.com/o/pixel", true)]
    [InlineData("https://example.com/track/email", true)]
    [InlineData("https://example.com/trk/open", true)]
    [InlineData("https://example.com/pixel/1x1.gif", true)]
    [InlineData("https://example.com/px/view", true)]
    [InlineData("https://example.com/e2t/o/abcdef", true)]
    [InlineData("https://example.com/e2t/c/abcdef", true)]
    [InlineData("https://example.com/beacon/view", true)]
    [InlineData("https://example.com/q/tracking", true)]
    public void IsTrackingPixel_WithTrackingPathPattern_ReturnsTrue(string url, bool expected)
    {
        var result = TrackingPixelDetector.IsTrackingPixel(url);

        result.Should().Be(expected);
    }

    #endregion

    #region Legitimate Image Preservation Tests

    [Theory]
    [InlineData("https://example.com/images/photo.jpg")]
    [InlineData("https://cdn.company.com/assets/logo.png")]
    [InlineData("https://storage.googleapis.com/bucket/image.gif")]
    [InlineData("https://i.imgur.com/abc123.png")]
    [InlineData("https://example.com/products/product-image.jpg")]
    [InlineData("https://example.com/uploads/2024/photo.png")]
    [InlineData("https://example.com/static/banner.gif")]
    public void IsTrackingPixel_WithLegitimateImageUrl_ReturnsFalse(string url)
    {
        var result = TrackingPixelDetector.IsTrackingPixel(url);

        result.Should().BeFalse($"'{url}' should not be detected as tracking pixel");
    }

    [Theory]
    [InlineData("cid:image001@localpart")]
    [InlineData("cid:attachment@domain.com")]
    [InlineData("CID:IMAGE@TEST")]
    public void IsTrackingPixel_WithCidReference_ReturnsFalse(string url)
    {
        var result = TrackingPixelDetector.IsTrackingPixel(url);

        result.Should().BeFalse("CID references are embedded images, not tracking pixels");
    }

    [Theory]
    [InlineData("data:image/png;base64,iVBORw0KGgo")]
    [InlineData("data:image/gif;base64,R0lGODlh")]
    [InlineData("DATA:image/jpeg;base64,/9j/4AAQ")]
    public void IsTrackingPixel_WithDataUri_ReturnsFalse(string url)
    {
        var result = TrackingPixelDetector.IsTrackingPixel(url);

        result.Should().BeFalse("Data URIs are embedded images, not tracking pixels");
    }

    #endregion

    #region Edge Case Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsTrackingPixel_WithNullOrEmptyInput_ReturnsFalse(string? url)
    {
        var result = TrackingPixelDetector.IsTrackingPixel(url);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("relative/path/image.png")]
    [InlineData("/absolute/path/image.png")]
    public void IsTrackingPixel_WithInvalidUrl_ReturnsFalse(string url)
    {
        var result = TrackingPixelDetector.IsTrackingPixel(url);

        result.Should().BeFalse("Invalid URLs should be preserved (conservative approach)");
    }

    [Theory]
    [InlineData("https://CT.SENDGRID.NET/wf/open")]
    [InlineData("https://Ct.SendGrid.Net/WF/OPEN")]
    [InlineData("HTTPS://EMLTRK.COM/TRACK")]
    public void IsTrackingPixel_IsCaseInsensitive(string url)
    {
        var result = TrackingPixelDetector.IsTrackingPixel(url);

        result.Should().BeTrue("Domain and path matching should be case-insensitive");
    }

    [Theory]
    [InlineData("ftp://sendgrid.net/pixel")]
    [InlineData("file:///path/to/image.png")]
    public void IsTrackingPixel_WithNonHttpScheme_ReturnsFalse(string url)
    {
        var result = TrackingPixelDetector.IsTrackingPixel(url);

        result.Should().BeFalse("Only HTTP/HTTPS URLs should be checked for tracking");
    }

    #endregion

    #region User-Reported Tracking URLs

    [Fact]
    public void IsTrackingPixel_WithIndiegogoTrackingUrl_ReturnsTrue()
    {
        // Exact pattern from user's report
        var url = "https://e.p.indiegogo.com/o/p/1416:c4c9f9735fcc5c13e1c1edd4f14dbcf3:d240930:62b48fc4510d514ee10eecdf:1727697280694/eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJpYXQiOjE3Mjc2OTcyODF9.wlVTB1UxAFroAcNU-P5KcSidhqpeCBKXyOXUARBxGFg";

        var result = TrackingPixelDetector.IsTrackingPixel(url);

        result.Should().BeTrue("Indiegogo e.p. subdomain with /o/p/ path is tracking");
    }

    [Fact]
    public void IsTrackingPixel_WithCordialTrackingUrl_ReturnsTrue()
    {
        // Exact pattern from user's report
        var url = "http://track.sp.crdl.io/q/Y3EMFNpDIZc5NHN2aLwhrQ~~/AAAABAA~/RgRo3RaBPlcHY29yZGlhbEIKZuyBkfpmfS142VIXYXBvbG9uQHRvcnFzb2Z0d2FyZS5jb21YBAAAAAM~";

        var result = TrackingPixelDetector.IsTrackingPixel(url);

        result.Should().BeTrue("Cordial track.sp.crdl.io with /q/ path is tracking");
    }

    [Fact]
    public void IsTrackingPixel_WithMicrosoftAzureTrackingPixelUrl_ReturnsTrue()
    {
        // Microsoft Azure SafeLink tracking pixel URL pattern
        var url = "https://nam.safelink.emails.azure.net/trackingpixel/?p=bT1mMmEzZDFiOS0xYmI0LTQ1M2ItODY2ZS0yMmI1M2QxZTY0M2Umcz0wMDAwMDAwMC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDAmdT1hZW8%3D";

        var result = TrackingPixelDetector.IsTrackingPixel(url);

        result.Should().BeTrue("Azure SafeLink /trackingpixel/ path should be detected as tracking");
    }

    [Theory]
    [InlineData("https://example.com/trackingpixel", true)]
    [InlineData("https://example.com/trackingpixel/", true)]
    [InlineData("https://example.com/trackingpixel/?id=123", true)]
    [InlineData("https://example.com/img/t/view", true)]
    [InlineData("https://example.com/tr/t/abc", true)]
    public void IsTrackingPixel_WithAdditionalTrackingPaths_ReturnsTrue(string url, bool expected)
    {
        var result = TrackingPixelDetector.IsTrackingPixel(url);

        result.Should().Be(expected);
    }

    #endregion
}
