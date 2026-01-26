using M365MailMirror.Infrastructure.Transform;

namespace M365MailMirror.UnitTests.Transform;

/// <summary>
/// Unit tests for MarkdownCleaningHelper which handles cleaning text content
/// during EML to Markdown transformation.
/// </summary>
public class MarkdownCleaningHelperTests
{
    #region CleanCidReferences Tests

    [Theory]
    [InlineData("[cid:image001.gif@01CA8DDC.A40BF8D0]", "")]
    [InlineData("[cid:image003.gif@01ABCDEF.12345678]", "")]
    [InlineData("Before [cid:img@xxx] After", "Before  After")]
    [InlineData("Line1\n[cid:test@abc]\nLine2", "Line1\n\nLine2")]
    public void CleanCidReferences_WithBracketedCidReferences_RemovesThem(string input, string expected)
    {
        var result = MarkdownCleaningHelper.CleanCidReferences(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("cid:inline@abc123", "")]
    [InlineData("Text cid:image@xyz.domain before", "Text  before")]
    public void CleanCidReferences_WithUnbracketedCidReferences_RemovesThem(string input, string expected)
    {
        var result = MarkdownCleaningHelper.CleanCidReferences(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Hello World", "Hello World")]
    [InlineData("Just some text", "Just some text")]
    [InlineData("Link: http://example.com", "Link: http://example.com")]
    public void CleanCidReferences_WithoutCidReferences_ReturnsUnchanged(string input, string expected)
    {
        var result = MarkdownCleaningHelper.CleanCidReferences(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void CleanCidReferences_WithNullOrEmpty_ReturnsInput(string? input, string? expected)
    {
        var result = MarkdownCleaningHelper.CleanCidReferences(input!);

        result.Should().Be(expected);
    }

    [Fact]
    public void CleanCidReferences_WithMultipleCidReferences_RemovesAll()
    {
        var input = "[cid:image001.gif@01CA8DDC.A40BF8D0]\n\nSome text\n\n[cid:image002.png@01CA8DDC.B50CF9E1]";

        var result = MarkdownCleaningHelper.CleanCidReferences(input);

        result.Should().NotContain("cid:");
        result.Should().Contain("Some text");
    }

    [Theory]
    [InlineData("[CID:IMAGE@123]", "")]
    [InlineData("[Cid:Test@ABC]", "")]
    public void CleanCidReferences_IsCaseInsensitive(string input, string expected)
    {
        var result = MarkdownCleaningHelper.CleanCidReferences(input);

        result.Should().Be(expected);
    }

    #endregion

    #region CleanOutlookStyleLinks Tests

    [Theory]
    [InlineData("Click<http://example.com>", "[Click](http://example.com)")]
    [InlineData("Visit<https://secure.example.com>", "[Visit](https://secure.example.com)")]
    public void CleanOutlookStyleLinks_WithHttpLinks_ConvertsToMarkdown(string input, string expected)
    {
        var result = MarkdownCleaningHelper.CleanOutlookStyleLinks(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("www.test.com<http://www.test.com>", "[www.test.com](http://www.test.com)")]
    [InlineData("example.org<https://example.org>", "[example.org](https://example.org)")]
    public void CleanOutlookStyleLinks_WithDuplicatedUrls_ConvertsToMarkdown(string input, string expected)
    {
        var result = MarkdownCleaningHelper.CleanOutlookStyleLinks(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("email@test.com<mailto:email@test.com>", "[email@test.com](mailto:email@test.com)")]
    [InlineData("contact<mailto:support@company.com>", "[contact](mailto:support@company.com)")]
    public void CleanOutlookStyleLinks_WithMailtoLinks_ConvertsToMarkdown(string input, string expected)
    {
        var result = MarkdownCleaningHelper.CleanOutlookStyleLinks(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Hello World", "Hello World")]
    [InlineData("[Already Markdown](http://example.com)", "[Already Markdown](http://example.com)")]
    [InlineData("Plain text with no links", "Plain text with no links")]
    public void CleanOutlookStyleLinks_WithoutOutlookLinks_ReturnsUnchanged(string input, string expected)
    {
        var result = MarkdownCleaningHelper.CleanOutlookStyleLinks(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void CleanOutlookStyleLinks_WithNullOrEmpty_ReturnsInput(string? input, string? expected)
    {
        var result = MarkdownCleaningHelper.CleanOutlookStyleLinks(input!);

        result.Should().Be(expected);
    }

    [Fact]
    public void CleanOutlookStyleLinks_WithMultipleLinks_ConvertsAll()
    {
        var input = "Visit site1<http://site1.com> and site2<https://site2.com>";

        var result = MarkdownCleaningHelper.CleanOutlookStyleLinks(input);

        result.Should().Be("Visit [site1](http://site1.com) and [site2](https://site2.com)");
    }

    [Fact]
    public void CleanOutlookStyleLinks_WithMixedContent_ConvertsOnlyLinks()
    {
        var input = "Check out link<http://example.com> for more info.\nPlain text here.";

        var result = MarkdownCleaningHelper.CleanOutlookStyleLinks(input);

        result.Should().Contain("[link](http://example.com)");
        result.Should().Contain("for more info");
        result.Should().Contain("Plain text here");
    }

    #endregion

    #region StripHtml Tests

    [Theory]
    [InlineData("<p>Hello</p>", "Hello")]
    [InlineData("<div>Content</div>", "Content")]
    [InlineData("<span>Text</span>", "Text")]
    public void StripHtml_WithSimpleTags_RemovesThem(string input, string expected)
    {
        var result = MarkdownCleaningHelper.StripHtml(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("<div><p>Nested</p></div>", "Nested")]
    [InlineData("<span><span>Double</span></span>", "Double")]
    [InlineData("<div><span><p>Triple</p></span></div>", "Triple")]
    public void StripHtml_WithNestedTags_RemovesAllTags(string input, string expected)
    {
        var result = MarkdownCleaningHelper.StripHtml(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("&amp;", "&")]
    [InlineData("&lt;", "<")]
    [InlineData("&gt;", ">")]
    [InlineData("&#39;", "'")]
    [InlineData("&quot;", "\"")]
    public void StripHtml_WithHtmlEntities_DecodesEntities(string input, string expected)
    {
        var result = MarkdownCleaningHelper.StripHtml(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void StripHtml_WithNbsp_DecodesAndDoesNotContainOriginal()
    {
        var result = MarkdownCleaningHelper.StripHtml("Text&nbsp;here");

        // &nbsp; should be decoded and not remain as literal "&nbsp;"
        result.Should().NotContain("&nbsp;");
        result.Should().Contain("Text");
        result.Should().Contain("here");
    }

    [Fact]
    public void StripHtml_WithMixedContent_StripsTagsAndDecodesEntities()
    {
        var input = "<p>Test &amp; Example &lt;tag&gt;</p>";

        var result = MarkdownCleaningHelper.StripHtml(input);

        result.Should().Be("Test & Example <tag>");
    }

    [Theory]
    [InlineData("Line1\n\n\n\n\nLine2", "Line1\n\nLine2")]
    [InlineData("A\n\n\nB\n\n\n\nC", "A\n\nB\n\nC")]
    public void StripHtml_WithExcessiveNewlines_NormalizesToTwoNewlines(string input, string expected)
    {
        var result = MarkdownCleaningHelper.StripHtml(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void StripHtml_WithNullOrEmpty_ReturnsInput(string? input, string? expected)
    {
        var result = MarkdownCleaningHelper.StripHtml(input!);

        result.Should().Be(expected);
    }

    [Fact]
    public void StripHtml_WithLeadingTrailingWhitespace_Trims()
    {
        var input = "   <p>Content</p>   ";

        var result = MarkdownCleaningHelper.StripHtml(input);

        result.Should().Be("Content");
    }

    #endregion

    #region DecodeHtmlEntities Tests

    [Theory]
    [InlineData("Test &amp; Subject", "Test & Subject")]
    [InlineData("Test &lt;Important&gt;", "Test <Important>")]
    [InlineData("Test &#39;Quoted&#39;", "Test 'Quoted'")]
    [InlineData("No entities here", "No entities here")]
    public void DecodeHtmlEntities_WithEntities_DecodesCorrectly(string input, string expected)
    {
        var result = MarkdownCleaningHelper.DecodeHtmlEntities(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void DecodeHtmlEntities_WithNullOrEmpty_ReturnsInput(string? input, string? expected)
    {
        var result = MarkdownCleaningHelper.DecodeHtmlEntities(input!);

        result.Should().Be(expected);
    }

    [Fact]
    public void DecodeHtmlEntities_WithMultipleEntities_DecodesAll()
    {
        var input = "Parents &amp; Friends Newsletter &#8212; Issue #5";

        var result = MarkdownCleaningHelper.DecodeHtmlEntities(input);

        result.Should().Contain("&");
        result.Should().Contain("—"); // Em dash
        result.Should().NotContain("&amp;");
        result.Should().NotContain("&#");
    }

    #endregion

    #region CleanTextForMarkdown Tests

    [Fact]
    public void CleanTextForMarkdown_WithPlainText_AppliesCleaningPipeline()
    {
        var input = "Visit site<http://example.com> and see [cid:image@test]";

        var result = MarkdownCleaningHelper.CleanTextForMarkdown(input, isHtml: false);

        result.Should().Contain("[site](http://example.com)");
        result.Should().NotContain("cid:");
    }

    [Fact]
    public void CleanTextForMarkdown_WithHtml_StripsHtmlAndCleans()
    {
        // Note: StripHtml removes anything in angle brackets, including <http://...> patterns
        // In real HTML emails, URLs are typically in <a href="..."> tags, not bare angle brackets
        var input = "<p>Visit the site &amp; see [cid:image@test]</p>";

        var result = MarkdownCleaningHelper.CleanTextForMarkdown(input, isHtml: true);

        result.Should().Contain("&"); // Entity decoded
        result.Should().Contain("Visit the site");
        result.Should().NotContain("cid:");
        result.Should().NotContain("<p>");
        result.Should().NotContain("&amp;");
    }

    [Theory]
    [InlineData(null, false, null)]
    [InlineData("", false, "")]
    [InlineData(null, true, null)]
    [InlineData("", true, "")]
    public void CleanTextForMarkdown_WithNullOrEmpty_ReturnsInput(string? input, bool isHtml, string? expected)
    {
        var result = MarkdownCleaningHelper.CleanTextForMarkdown(input!, isHtml);

        result.Should().Be(expected);
    }

    #endregion
}
