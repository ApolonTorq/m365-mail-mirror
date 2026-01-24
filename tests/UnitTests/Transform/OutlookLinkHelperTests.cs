using M365MailMirror.Infrastructure.Transform;

namespace M365MailMirror.UnitTests.Transform;

public class OutlookLinkHelperTests
{
    #region GenerateOutlookUrl Tests

    [Fact]
    public void GenerateOutlookUrl_WithImmutableId_ReturnsCorrectUrl()
    {
        var result = OutlookLinkHelper.GenerateOutlookUrl("ABC123", null);

        result.Should().Be("https://outlook.office.com/mail/deeplink/read/ABC123");
    }

    [Fact]
    public void GenerateOutlookUrl_WithSpecialCharacters_UrlEncodesId()
    {
        // ImmutableIds may contain special characters like +, /, =
        var result = OutlookLinkHelper.GenerateOutlookUrl("ABC123+/=", null);

        result.Should().Be("https://outlook.office.com/mail/deeplink/read/ABC123%2B%2F%3D");
    }

    [Fact]
    public void GenerateOutlookUrl_WithMailbox_IncludesMailboxInPath()
    {
        var result = OutlookLinkHelper.GenerateOutlookUrl("ABC123", "shared@example.com");

        result.Should().Be("https://outlook.office.com/mail/shared%40example.com/deeplink/read/ABC123");
    }

    [Fact]
    public void GenerateOutlookUrl_WithMailboxContainingSpecialChars_UrlEncodesMailbox()
    {
        var result = OutlookLinkHelper.GenerateOutlookUrl("ABC123", "user+tag@example.com");

        result.Should().Contain("user%2Btag%40example.com");
    }

    [Fact]
    public void GenerateOutlookUrl_NullImmutableId_ReturnsNull()
    {
        var result = OutlookLinkHelper.GenerateOutlookUrl(null, null);

        result.Should().BeNull();
    }

    [Fact]
    public void GenerateOutlookUrl_EmptyImmutableId_ReturnsNull()
    {
        var result = OutlookLinkHelper.GenerateOutlookUrl("", null);

        result.Should().BeNull();
    }

    [Fact]
    public void GenerateOutlookUrl_EmptyMailbox_UsesPersonalFormat()
    {
        var result = OutlookLinkHelper.GenerateOutlookUrl("ABC123", "");

        result.Should().Be("https://outlook.office.com/mail/deeplink/read/ABC123");
    }

    #endregion

    #region GenerateHtmlLink Tests

    [Fact]
    public void GenerateHtmlLink_WithValidId_ReturnsAnchorTag()
    {
        var result = OutlookLinkHelper.GenerateHtmlLink("ABC123", null);

        result.Should().Contain("<a href=");
        result.Should().Contain("View in Outlook");
        result.Should().Contain("target=\"_blank\"");
        result.Should().Contain("rel=\"noopener noreferrer\"");
    }

    [Fact]
    public void GenerateHtmlLink_WithValidId_ContainsCorrectUrl()
    {
        var result = OutlookLinkHelper.GenerateHtmlLink("ABC123", null);

        result.Should().Contain("https://outlook.office.com/mail/deeplink/read/ABC123");
    }

    [Fact]
    public void GenerateHtmlLink_WithMailbox_IncludesMailboxInUrl()
    {
        var result = OutlookLinkHelper.GenerateHtmlLink("ABC123", "shared@example.com");

        result.Should().Contain("shared%40example.com");
    }

    [Fact]
    public void GenerateHtmlLink_NullId_ReturnsEmptyString()
    {
        var result = OutlookLinkHelper.GenerateHtmlLink(null, null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GenerateHtmlLink_EmptyId_ReturnsEmptyString()
    {
        var result = OutlookLinkHelper.GenerateHtmlLink("", null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GenerateHtmlLink_ProperlyHtmlEncodesUrl()
    {
        // Even though the URL is already URL-encoded, we should HTML-encode it for the href attribute
        var result = OutlookLinkHelper.GenerateHtmlLink("ABC<>&123", null);

        // The < and > should be both URL-encoded (in the URL) and HTML-encoded (in the href)
        result.Should().NotContain("<>&");
    }

    #endregion

    #region GenerateMarkdownFrontMatter Tests

    [Fact]
    public void GenerateMarkdownFrontMatter_WithValidId_ReturnsYamlFormat()
    {
        var result = OutlookLinkHelper.GenerateMarkdownFrontMatter("ABC123", null);

        result.Should().StartWith("outlookUrl:");
        result.Should().Contain("https://outlook.office.com/mail/deeplink/read/ABC123");
        result.Should().EndWith("\n");
    }

    [Fact]
    public void GenerateMarkdownFrontMatter_WithMailbox_IncludesMailboxInUrl()
    {
        var result = OutlookLinkHelper.GenerateMarkdownFrontMatter("ABC123", "shared@example.com");

        result.Should().Contain("shared%40example.com");
    }

    [Fact]
    public void GenerateMarkdownFrontMatter_NullId_ReturnsEmptyString()
    {
        var result = OutlookLinkHelper.GenerateMarkdownFrontMatter(null, null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GenerateMarkdownFrontMatter_EmptyId_ReturnsEmptyString()
    {
        var result = OutlookLinkHelper.GenerateMarkdownFrontMatter("", null);

        result.Should().BeEmpty();
    }

    #endregion

    #region GenerateMarkdownDisplayLine Tests

    [Fact]
    public void GenerateMarkdownDisplayLine_WithValidId_ReturnsMarkdownLink()
    {
        var result = OutlookLinkHelper.GenerateMarkdownDisplayLine("ABC123", null);

        result.Should().Contain("[View in Outlook]");
        result.Should().Contain("(https://outlook.office.com/mail/deeplink/read/ABC123)");
        result.Should().EndWith("\n");
    }

    [Fact]
    public void GenerateMarkdownDisplayLine_WithMailbox_IncludesMailboxInUrl()
    {
        var result = OutlookLinkHelper.GenerateMarkdownDisplayLine("ABC123", "shared@example.com");

        result.Should().Contain("shared%40example.com");
    }

    [Fact]
    public void GenerateMarkdownDisplayLine_NullId_ReturnsEmptyString()
    {
        var result = OutlookLinkHelper.GenerateMarkdownDisplayLine(null, null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GenerateMarkdownDisplayLine_EmptyId_ReturnsEmptyString()
    {
        var result = OutlookLinkHelper.GenerateMarkdownDisplayLine("", null);

        result.Should().BeEmpty();
    }

    #endregion

    #region Real-World ImmutableId Format Tests

    [Fact]
    public void GenerateOutlookUrl_RealWorldImmutableId_HandlesCorrectly()
    {
        // Real ImmutableIds look like this (base64-encoded):
        var immutableId = "AAMkAGYxM2Y2ODY1LTYwZjMtNGFhOS1iYmJjLWRjYThhMGU2ZWEwNgAuAAAAAAB2kPTzhnVnQohyyLRCxZAAAQCQQvHzSQBySKWUt7H/FicJAAAhGpfkAAA=";

        var result = OutlookLinkHelper.GenerateOutlookUrl(immutableId, null);

        result.Should().NotBeNull();
        result.Should().StartWith("https://outlook.office.com/mail/deeplink/read/");
        // The / character should be URL-encoded
        result.Should().Contain("%2F"); // URL-encoded /
        // The = character should be URL-encoded
        result.Should().Contain("%3D"); // URL-encoded =
    }

    #endregion
}
