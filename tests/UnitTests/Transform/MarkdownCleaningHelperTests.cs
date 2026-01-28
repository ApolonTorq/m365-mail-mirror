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

    [Fact]
    public void CleanCidReferences_PreservesMarkdownImageSyntax()
    {
        // After converting HTML images to markdown, cid refs should be preserved in image syntax
        var input = "Some text ![image](cid:582141002@25042004-2A2D) more text";

        var result = MarkdownCleaningHelper.CleanCidReferences(input);

        // The markdown image syntax should remain intact
        result.Should().Contain("![image](cid:582141002@25042004-2A2D)");
    }

    [Fact]
    public void CleanCidReferences_PreservesMultipleMarkdownImages()
    {
        var input = "![img1](cid:image001@test) text ![img2](cid:image002@test)";

        var result = MarkdownCleaningHelper.CleanCidReferences(input);

        result.Should().Contain("![img1](cid:image001@test)");
        result.Should().Contain("![img2](cid:image002@test)");
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

    #region ConvertHtmlToMarkdown Tests

    [Theory]
    [InlineData("<p>Hello</p>", "Hello")]
    [InlineData("<div>Content</div>", "Content")]
    [InlineData("<span>Text</span>", "Text")]
    public void ConvertHtmlToMarkdown_WithSimpleTags_RemovesThem(string input, string expected)
    {
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("<div><p>Nested</p></div>", "Nested")]
    [InlineData("<span><span>Double</span></span>", "Double")]
    [InlineData("<div><span><p>Triple</p></span></div>", "Triple")]
    public void ConvertHtmlToMarkdown_WithNestedTags_RemovesAllTags(string input, string expected)
    {
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("&amp;", "&")]
    [InlineData("&lt;", "<")]
    [InlineData("&gt;", ">")]
    [InlineData("&#39;", "'")]
    [InlineData("&quot;", "\"")]
    public void ConvertHtmlToMarkdown_WithHtmlEntities_DecodesEntities(string input, string expected)
    {
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithNbsp_DecodesAndDoesNotContainOriginal()
    {
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown("Text&nbsp;here");

        // &nbsp; should be decoded and not remain as literal "&nbsp;"
        result.Should().NotContain("&nbsp;");
        result.Should().Contain("Text");
        result.Should().Contain("here");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithMixedContent_StripsTagsAndDecodesEntities()
    {
        var input = "<p>Test &amp; Example &lt;tag&gt;</p>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Be("Test & Example <tag>");
    }

    [Theory]
    [InlineData("Line1\n\n\n\n\nLine2", "Line1\n\nLine2")]
    [InlineData("A\n\n\nB\n\n\n\nC", "A\n\nB\n\nC")]
    public void ConvertHtmlToMarkdown_WithExcessiveNewlines_NormalizesToTwoNewlines(string input, string expected)
    {
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void ConvertHtmlToMarkdown_WithNullOrEmpty_ReturnsInput(string? input, string? expected)
    {
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input!);

        result.Should().Be(expected);
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithLeadingTrailingWhitespace_Trims()
    {
        var input = "   <p>Content</p>   ";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

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

    #region ConvertHtmlToMarkdown Performance and Safety Tests

    [Fact]
    public void ConvertHtmlToMarkdown_WithLargeContent_CompletesWithinReasonableTime()
    {
        // Simulate large content like mxGraph XML with embedded base64 images
        // This pattern mimics URL-encoded mxGraph content with data URIs
        var largeContent = string.Concat(Enumerable.Repeat(
            "<div>Some text with &lt;embedded&gt; content</div>",
            10000));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(largeContent);

        stopwatch.Stop();

        // Should complete within 5 seconds even with large content
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithMxGraphLikeUrlEncodedContent_CompletesQuickly()
    {
        // This mimics the problematic mxGraph XML with URL-encoded data URIs
        // that appeared in Outlook emails causing CPU issues
        var mxGraphPattern = "%3CmxGraphModel%3E%3Croot%3E%3CmxCell%20id%3D%220%22%2F%3E" +
            "image%3Ddata%3Aimage%2Fjpeg%2C%2F9j%2F4AAQSkZJRgABAQAAAQABAAD%2F2wCEAAkGBxQSEhUR";

        // Repeat to simulate large embedded base64 image data
        var largeContent = string.Concat(Enumerable.Repeat(mxGraphPattern, 5000));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(largeContent);

        stopwatch.Stop();

        // Should complete quickly since URL-encoded content doesn't match HTML tag pattern
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        result.Should().NotBeNull();
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithDeeplyNestedTags_RespectsIterationLimit()
    {
        // Create pathological deeply nested tags
        var nested = new string('<', 200) + "content" + new string('>', 200);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(nested);

        stopwatch.Stop();

        // Should complete within reasonable time due to iteration limit
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        result.Should().NotBeNull();
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithContentExceedingMaxLength_TruncatesAndCompletes()
    {
        // Create content larger than MaxContentLength
        var hugeContent = new string('a', MarkdownCleaningHelper.MaxContentLength + 1000);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(hugeContent);

        stopwatch.Stop();

        // Should complete quickly
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        result.Should().Contain("[Content truncated");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithManySmallTags_CompletesEfficiently()
    {
        // Create HTML with many small tags (common in complex email formatting)
        // Use fewer tags to stay under MaxContentLength (1MB)
        var manyTags = string.Concat(Enumerable.Range(0, 20000)
            .Select(i => $"<span class=\"s{i}\">text{i}</span>"));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(manyTags);

        stopwatch.Stop();

        // Should complete within reasonable time
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        result.Should().NotContain("<span");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithUnbalancedTags_HandlesGracefully()
    {
        // Unbalanced tags that might cause issues in naive implementations
        var unbalanced = "<div><span><p>Text</div></span></p>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(unbalanced);

        result.Should().Be("Text");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_MaxIterationsConstant_IsReasonable()
    {
        // Verify the constant is exposed and reasonable
        MarkdownCleaningHelper.MaxStripIterations.Should().BeGreaterThan(10);
        MarkdownCleaningHelper.MaxStripIterations.Should().BeLessThan(1000);
    }

    [Fact]
    public void ConvertHtmlToMarkdown_MaxContentLengthConstant_IsReasonable()
    {
        // Verify the constant is exposed and reasonable (between 100KB and 100MB)
        MarkdownCleaningHelper.MaxContentLength.Should().BeGreaterThanOrEqualTo(100 * 1024);
        MarkdownCleaningHelper.MaxContentLength.Should().BeLessThan(100 * 1024 * 1024);
    }

    #endregion

    #region HTML Semantic Structure Tests

    // These tests verify that HTML semantic structures (tables, lists, bold, images)
    // are properly converted to Markdown equivalents rather than being stripped entirely.

    [Fact]
    public void ConvertHtmlToMarkdown_WithSimpleTable_ShouldConvertToMarkdownTable()
    {
        // HTML table from the Iris Daily Progress Report email
        var input = @"<table>
<tr>
<td>Iris hours worked today:</td>
<td>6.5</td>
</tr>
<tr>
<td>Iris hours worked month to date:</td>
<td>14.5</td>
</tr>
</table>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Expected: Markdown table format with columns separated by pipes
        result.Should().Contain("| Iris hours worked today: | 6.5 |");
        result.Should().Contain("| Iris hours worked month to date: | 14.5 |");
        // Must include header separator for proper markdown table rendering
        result.Should().Contain("| --- | --- |");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithTable_ShouldIncludeHeaderSeparatorAfterFirstRow()
    {
        var input = @"<table>
<tr><td>Header 1</td><td>Header 2</td><td>Header 3</td></tr>
<tr><td>Data 1</td><td>Data 2</td><td>Data 3</td></tr>
</table>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // The separator must have the same number of columns as the first row
        result.Should().Contain("| --- | --- | --- |");
        // First row should come before separator
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Contain("Header 1");
        lines[1].Should().Contain("---");
        lines[2].Should().Contain("Data 1");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithTableContainingNumberedList_ShouldPreserveListFormat()
    {
        // Outlook-style numbered list implemented as a table (number in first column, text in second)
        // This is the actual structure from the Iris Daily Progress Report email
        var input = @"<table>
<tr>
<td><ol type=""1""><li>&nbsp;</li></ol></td>
<td>Changed the remaining query methods in the Agency class to use the new TorqQueryAttribute approach.</td>
</tr>
<tr>
<td><ol type=""1"" start=""2""><li>&nbsp;</li></ol></td>
<td>Encountered a fault where a duplicate Agent name would cause an exception in agent payments.</td>
</tr>
</table>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Expected: Numbered list with content on same line as number
        result.Should().Contain("1. Changed the remaining query methods");
        result.Should().Contain("2. Encountered a fault where a duplicate Agent name");
    }

    [Theory]
    [InlineData("<b>Tasks:</b>", "**Tasks:**")]
    [InlineData("<b>Requests:</b>", "**Requests:**")]
    [InlineData("<b>Questions:</b>", "**Questions:**")]
    [InlineData("<b>Notes:</b>", "**Notes:**")]
    public void ConvertHtmlToMarkdown_WithBoldText_ShouldConvertToMarkdownBold(string input, string expected)
    {
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Expected: Bold text converted to markdown ** syntax
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("<strong>Important</strong>", "**Important**")]
    [InlineData("<strong>Warning:</strong> Read carefully", "**Warning:** Read carefully")]
    public void ConvertHtmlToMarkdown_WithStrongText_ShouldConvertToMarkdownBold(string input, string expected)
    {
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Expected: Strong text converted to markdown ** syntax
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("<b>Saturday,\n April 24, 2004</b>", "**Saturday, April 24, 2004**")]
    [InlineData("<b>Line1\nLine2</b>", "**Line1 Line2**")]
    [InlineData("<b>  Multiple   spaces  </b>", "**Multiple spaces**")]
    public void ConvertHtmlToMarkdown_WithBoldContainingWhitespace_NormalizesWhitespace(string input, string expected)
    {
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("<b>Text</b><b>More</b>", "**Text** **More**")]
    [InlineData("<b>First</b><o:p></o:p><b>Second</b>", "**First** **Second**")]
    public void ConvertHtmlToMarkdown_WithConsecutiveBoldTags_DoesNotProduceExtraAsterisks(string input, string expected)
    {
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Should not have 4 or more consecutive asterisks
        result.Should().NotContain("****");
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("<o:p></o:p>", "")]
    [InlineData("<o:p>&nbsp;</o:p>", "")]
    [InlineData("<st1:date>April 24</st1:date>", "April 24")]
    [InlineData("Text <o:p>content</o:p> more", "Text content more")]
    public void ConvertHtmlToMarkdown_WithOfficeXmlTags_StripsOrPreservesContent(string input, string expected)
    {
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithOutlookDatePattern_ProducesCleanOutput()
    {
        // This is the actual pattern from the Iris Daily Progress Report that was causing issues
        var input = @"<b>Saturday,
 April 24, 2004</b><b><o:p></o:p></b><b><o:p></o:p></b><o:p></o:p>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Should produce clean bold date without extra asterisks or newlines
        result.Should().Contain("**Saturday, April 24, 2004**");
        result.Should().NotContain("****");
        result.Should().NotContain("\n");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithInlineImageInContent_ShouldPreserveImageReference()
    {
        // Inline image from the Iris Daily Progress Report email
        var input = @"<p>The rest of the day was spent working on the report.</p>
<p><img src=""cid:582141002@25042004-2A2D"" width=""800"" height=""885""></p>
<p>More text after the image.</p>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Expected: Image should be preserved as a cid reference or placeholder
        // (The actual path resolution happens elsewhere, but the reference should remain)
        result.Should().Contain("cid:582141002@25042004-2A2D");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithComplexOutlookEmailStructure_ShouldPreserveReadability()
    {
        // Combined structure similar to the actual Iris Daily Progress Report
        var input = @"<p><b>Tasks:</b></p>
<table>
<tr>
<td><ol type=""1""><li>&nbsp;</li></ol></td>
<td>First task description with <img src=""cid:image001""> inline.</td>
</tr>
</table>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Expected behavior for a complete transformation:
        // 1. Bold heading should be preserved
        result.Should().Contain("**Tasks:**");
        // 2. List format should be preserved
        result.Should().Contain("1. First task description");
        // 3. Image reference should be preserved
        result.Should().Contain("cid:image001");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithImageInsideOutlookListTable_ShouldPreserveImageReference()
    {
        // Exact structure from the Iris Daily Progress Report email
        // Note: img tag has id, height attributes BEFORE src
        var input = @"<table>
<tr>
<td><ol type=""1"" start=""5""><li>&nbsp;</li></ol></td>
<td>
<p>The rest of the day was spent working on the report.</p>
<p><img id=""x1"" height=""885"" src=""cid:582141002@25042004-2A2D"" width=""800""></p>
<p>&nbsp;</p>
<p><img id=""x2"" height=""697"" src=""cid:582141002@25042004-2A34"" width=""770""></p>
</td>
</tr>
</table>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // List item should be numbered correctly
        result.Should().Contain("5. The rest of the day");
        // Both images should be preserved with proper markdown syntax
        result.Should().Contain("![image](cid:582141002@25042004-2A2D)");
        result.Should().Contain("![image](cid:582141002@25042004-2A34)");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithImageAttributes_ShouldMatchCorrectly()
    {
        // Test that image pattern works when src is not the first attribute
        var input = @"<img id=""_x0000_i1084"" height=""885"" src=""cid:test123"" width=""800"">";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Contain("![image](cid:test123)");
    }

    #endregion

    #region CSS and Script Stripping Tests

    [Fact]
    public void ConvertHtmlToMarkdown_WithStyleTag_StripsStyleAndContent()
    {
        // Outlook emails often contain embedded CSS that shouldn't appear in markdown
        var input = @"<style type=""text/css"">
.MsoNormal { font-size: 12pt; margin: 0; }
P { color: blue; }
</style>
<p>Actual content here</p>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().NotContain(".MsoNormal");
        result.Should().NotContain("font-size");
        result.Should().NotContain("color: blue");
        result.Should().Contain("Actual content here");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithHeadTag_StripsHeadAndContent()
    {
        // The <head> section with title and meta tags should be stripped
        var input = @"<html>
<head>
<title>Message</title>
<meta name=""Generator"" content=""Microsoft Word"">
</head>
<body>
<p>Body content</p>
</body>
</html>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().NotContain("Message");
        result.Should().NotContain("Generator");
        result.Should().NotContain("Microsoft Word");
        result.Should().Contain("Body content");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithScriptTag_StripsScriptAndContent()
    {
        var input = @"<p>Before</p>
<script type=""text/javascript"">
alert('hello');
function test() { return 1; }
</script>
<p>After</p>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().NotContain("alert");
        result.Should().NotContain("function test");
        result.Should().Contain("Before");
        result.Should().Contain("After");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithHtmlComments_StripsComments()
    {
        var input = @"<p>Visible</p>
<!-- This is a comment that should be removed -->
<p>Also visible</p>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().NotContain("This is a comment");
        result.Should().Contain("Visible");
        result.Should().Contain("Also visible");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithConditionalComments_StripsConditionalComments()
    {
        // IE/Office conditional comments like <!--[if !mso]> ... <![endif]-->
        var input = @"<p>Before</p>
<!--[if !mso]>
<style>v\:* { BEHAVIOR: url(#default#VML) }</style>
<![endif]-->
<p>After</p>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().NotContain("BEHAVIOR");
        result.Should().NotContain("[if !mso]");
        result.Should().Contain("Before");
        result.Should().Contain("After");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithComplexOutlookHtml_ProducesCleanMarkdown()
    {
        // Simulates a typical Outlook email with all the cruft
        var input = @"<html>
<head>
<title>Message</title>
<meta content=""Microsoft Word"" name=""Originator"">
</head>
<body>
<!--[if !mso]>
<style>v\:* { BEHAVIOR: url(#default#VML) }</style>
<![endif]-->
<style>
.MsoNormal { font-size: 12pt; }
</style>
<p><b>Tasks:</b></p>
<table>
<tr><td>Item 1</td><td>Value 1</td></tr>
</table>
</body>
</html>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Should NOT contain any of the metadata/style content
        result.Should().NotContain("Microsoft Word");
        result.Should().NotContain(".MsoNormal");
        result.Should().NotContain("BEHAVIOR");
        result.Should().NotContain("font-size");

        // Should contain the actual content converted to markdown
        result.Should().Contain("**Tasks:**");
        result.Should().Contain("| Item 1 | Value 1 |");
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
        // Note: ConvertHtmlToMarkdown removes anything in angle brackets, including <http://...> patterns
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

    #region HR Tag Conversion Tests

    [Theory]
    [InlineData("<hr>", "---")]
    [InlineData("<hr/>", "---")]
    [InlineData("<hr />", "---")]
    [InlineData("<HR>", "---")]
    public void ConvertHtmlToMarkdown_WithSimpleHrTag_ConvertsToMarkdownSeparator(string input, string expected)
    {
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithHrTagWithAttributes_ConvertsToMarkdownSeparator()
    {
        // From the actual Outlook email - HR with tabindex and other attributes
        var input = @"<hr tabindex=""-1"" align=""center"" width=""100%"" size=""2"">";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Be("---");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithHrBetweenContent_PreservesSeparator()
    {
        var input = @"<p>Please advise, in due course.</p>
<hr tabindex=""-1"">
<p><b>From:</b> John Doe</p>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Contain("Please advise");
        result.Should().Contain("---");
        result.Should().Contain("**From:**");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithMultipleHrTags_ConvertsAll()
    {
        var input = @"<p>Section 1</p>
<hr>
<p>Section 2</p>
<hr>
<p>Section 3</p>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Count occurrences of "---"
        var separatorCount = result.Split("---").Length - 1;
        separatorCount.Should().Be(2);
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithOutlookReplyChainHr_ConvertsSeparators()
    {
        // This is the actual Outlook reply chain pattern
        var input = @"<div class=""OutlookMessageHeader"" dir=""ltr"">
<hr tabindex=""-1"">
<font face=""Tahoma"" size=""2""><b>From:</b> sender@example.com</font>
</div>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Contain("---");
        result.Should().Contain("**From:**");
    }

    #endregion

    #region Image Separation in List Items Tests

    [Theory]
    [InlineData(@"<img src=""cid:test@123"">")]
    [InlineData(@"<img src=""cid:test@123"" width=""100"">")]
    [InlineData(@"  <img src=""cid:test@123"">  ")]
    [InlineData(@"&nbsp;<img src=""cid:test@123"">&nbsp;")]
    public void ImageOnlyPattern_ShouldMatchImgOnlyContent(string content)
    {
        // This tests the internal logic that detects image-only paragraphs
        var pattern = new System.Text.RegularExpressions.Regex(
            @"^\s*(<img\s[^>]*>|\s|&nbsp;)+\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

        pattern.IsMatch(content).Should().BeTrue($"Pattern should match '{content}'");
    }

    [Theory]
    [InlineData(@"Some text <img src=""cid:test@123"">")]
    [InlineData(@"<img src=""cid:test@123""> and more text")]
    [InlineData(@"Just text")]
    public void ImageOnlyPattern_ShouldNotMatchMixedContent(string content)
    {
        var pattern = new System.Text.RegularExpressions.Regex(
            @"^\s*(<img\s[^>]*>|\s|&nbsp;)+\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

        pattern.IsMatch(content).Should().BeFalse($"Pattern should not match '{content}'");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithImageInSeparateParagraphInListCell_ShouldSeparateImageFromListItem()
    {
        // Image is in its own <p> tag after the list item text - should be on separate line
        // Note: Real Outlook emails wrap images in <font><span> tags for styling
        var input = @"<table>
<tr>
<td><ol type=""1"" start=""8""><li>&nbsp;</li></ol></td>
<td>
<p>Spent time on the icon. Will leave a decision on it once all the other icons have been replaced.</p>
<p><font face=""Arial"" size=""2""><span style=""FONT-SIZE: 10pt""><img src=""cid:421002300@31012006-05B3"" width=""518"" height=""324""></span></font></p>
</td>
</tr>
</table>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // The list item text should be on one line
        result.Should().Contain("8. Spent time on the icon");
        // The image should be present
        result.Should().Contain("![image](cid:421002300@31012006-05B3)");
        // The image should NOT be on the same line as the list item number
        // In other words, the "8." and the "![image]" should be on different lines
        var nonEmptyLines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var listItemLine = nonEmptyLines.FirstOrDefault(l => l.StartsWith("8.", StringComparison.Ordinal));
        listItemLine.Should().NotBeNull();
        listItemLine.Should().NotContain("![image]");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithImageInlineSameAsParagraph_KeepsOnSameLine()
    {
        // Image is in the same <p> tag as text - should stay together
        var input = @"<table>
<tr>
<td><ol type=""1"" start=""5""><li>&nbsp;</li></ol></td>
<td>
<p>Here is the icon: <img src=""cid:icon@test""> as you can see.</p>
</td>
</tr>
</table>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // The image should be inline with the text
        result.Should().Contain("5. Here is the icon: ![image](cid:icon@test) as you can see.");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithMultipleImagesInSeparateParagraphs_ShouldSeparateEach()
    {
        // Multiple images, each in their own paragraph
        var input = @"<table>
<tr>
<td><ol type=""1"" start=""5""><li>&nbsp;</li></ol></td>
<td>
<p>The report screenshots:</p>
<p><img src=""cid:image001@test""></p>
<p><img src=""cid:image002@test""></p>
</td>
</tr>
</table>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Should have the list item
        result.Should().Contain("5. The report screenshots:");
        // Both images should be present
        result.Should().Contain("![image](cid:image001@test)");
        result.Should().Contain("![image](cid:image002@test)");
        // Images should be on separate lines from the list item
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var listItemLine = lines.FirstOrDefault(l => l.StartsWith("5.", StringComparison.Ordinal));
        listItemLine.Should().NotBeNull();
        listItemLine.Should().NotContain("![image]");
    }

    #endregion

    #region Bold Text with Nested HTML Tags Tests

    [Theory]
    [InlineData("<b><font> DC</font></b>", "**DC**")]
    [InlineData("<b><span> WA 6919</span></b>", "**WA 6919**")]
    [InlineData("<b><font size=\"1\"><span style=\"color:navy\"> Leading space</span></font></b>", "**Leading space**")]
    public void ConvertHtmlToMarkdown_WithBoldContainingNestedTagsAndLeadingSpace_TrimsSpaceCorrectly(string input, string expected)
    {
        // Bug: When bold tags contain nested HTML tags (font, span) with leading whitespace,
        // the Trim() only affects the outer string which starts with '<', not the inner text.
        // Result: "** DC**" instead of "**DC**" - invalid Markdown bold syntax.
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithOutlookSignaturePattern_ProducesValidBold()
    {
        // Exact pattern from the problematic email signature
        // Multiple adjacent bold tags with nested font/span containing leading spaces
        var input = @"<b><font size=""1"" face=""Arial""><span style=""font-size:7.5pt;font-family:Arial;color:navy"">JOONDALUP</span></font></b><b><font size=""1"" face=""Arial""><span style=""font-size:7.5pt;font-family:Arial;color:navy"">
 DC</span></font></b><b><font size=""1"" face=""Arial""><span style=""font-size:7.5pt;font-family:Arial;color:navy""> WA 6919</span></font></b>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Should NOT produce "** DC**" - that's invalid Markdown (space after opening **)
        // Use regex to check for invalid patterns: ** followed by space then non-**
        result.Should().NotMatchRegex(@"\*\* [^*]");
        // Should contain properly formatted bold text
        result.Should().Contain("**JOONDALUP**");
        result.Should().Contain("**DC**");
        result.Should().Contain("**WA 6919**");
    }

    #endregion

    #region Paragraph Whitespace Normalization Tests

    [Fact]
    public void ConvertHtmlToMarkdown_WithParagraphContainingHardLineBreaks_CollapsesToSingleParagraph()
    {
        // Bug: HTML source has hard line breaks for readability (not <br> tags),
        // which are being preserved as newlines in the output instead of collapsed to spaces.
        var input = @"<p>This e-mail and any attachments to it (the ""Communication"") is confidential and is for the use only of the intended recipient. The Communication
 may contain copyright material of Company Pty Ltd ABN 83 095 223 620, or of third parties. If you are not the intended recipient of the Communication, please notify the sender immediately by return e-mail, delete the Communication, and do not read,
 copy, print, retransmit, store or act in reliance on the Communication.</p>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // The output should be a single paragraph without line breaks
        result.Should().NotContain("\n");
        // Should contain all the text flowing together
        result.Should().Contain("The Communication may contain");
        result.Should().Contain("please notify the sender");
    }

    [Theory]
    [InlineData("<p>Line1\n Line2</p>", "Line1 Line2")]
    [InlineData("<p>Word1\r\n Word2</p>", "Word1 Word2")]
    [InlineData("<p>Text\n  with\n   indented\n    lines</p>", "Text with indented lines")]
    public void ConvertHtmlToMarkdown_WithLineBreaksInParagraphs_NormalizesToSpaces(string input, string expected)
    {
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithBrTags_PreservesLineBreaks()
    {
        // <br> tags should produce actual line breaks, unlike source code formatting
        var input = @"<p>Line 1<br>Line 2<br/>Line 3</p>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // <br> tags should be converted to actual line breaks
        result.Should().Contain("Line 1");
        result.Should().Contain("Line 2");
        result.Should().Contain("Line 3");
    }

    #endregion

    #region Table Separation Tests

    [Fact]
    public void ConvertHtmlToMarkdown_WithTableAfterParagraph_ShouldHaveBlankLineBefore()
    {
        // Tables need blank lines before them to render properly in markdown
        var input = @"<p>Some text before the table</p>
<table>
<tr><td>Header 1</td><td>Header 2</td></tr>
<tr><td>Data 1</td><td>Data 2</td></tr>
</table>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // The table should be on a separate line from the preceding text
        // Split by lines and check that "Some text" and "|" are on different lines
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var textLine = lines.FirstOrDefault(l => l.Contains("Some text"));
        var tableLine = lines.FirstOrDefault(l => l.StartsWith('|'));

        textLine.Should().NotBeNull();
        tableLine.Should().NotBeNull();
        // Text line should NOT contain the table pipe character
        textLine.Should().NotContain("|");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithTableBetweenParagraphs_ShouldHaveBlankLinesAroundIt()
    {
        // Tables need blank lines before and after them to render properly in markdown
        var input = @"<p>Text before</p>
<table>
<tr><td>Cell</td></tr>
</table>
<p>Text after</p>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // The table should be separated from surrounding text
        result.Should().Contain("Text before");
        result.Should().Contain("| Cell |");
        result.Should().Contain("Text after");

        // None of these should be on the same line
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var beforeLine = lines.FirstOrDefault(l => l.Contains("Text before"));
        var afterLine = lines.FirstOrDefault(l => l.Contains("Text after"));

        beforeLine.Should().NotContain("|");
        afterLine.Should().NotContain("|");
    }

    #endregion

    #region Paragraph Tag Conversion Tests

    [Fact]
    public void ConvertHtmlToMarkdown_WithParagraphTags_ShouldPreserveParagraphBreaks()
    {
        // Bug: <p> tags are being stripped without adding paragraph breaks,
        // causing all content to collapse into one continuous blob of text.
        // Example: An email with multiple paragraphs separated by <p> tags
        // should maintain paragraph separation in the markdown output.
        var input = @"<p>Hi John,</p>
<p>I am writing to follow up on our meeting.</p>
<p><b>Action Items:</b></p>
<p>Please review the attached document.</p>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Each paragraph should be on a separate line (separated by blank lines in markdown)
        // The output should NOT be one continuous blob of text
        result.Should().Contain("Hi John,");
        result.Should().Contain("I am writing to follow up");
        result.Should().Contain("**Action Items:**");
        result.Should().Contain("Please review");

        // Key assertion: paragraphs should be separated by newlines, not run together
        // If they're all on one line, this would fail
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().BeGreaterThan(1, "Multiple paragraphs should produce multiple lines, not one blob");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithClosingParagraphTags_ShouldAddNewlines()
    {
        // </p> marks the end of a paragraph and should result in a line break
        var input = @"<p>First paragraph.</p><p>Second paragraph.</p>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // The two paragraphs should be on separate lines
        result.Should().Contain("First paragraph.");
        result.Should().Contain("Second paragraph.");

        // They should NOT be on the same line
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var firstLine = lines.FirstOrDefault(l => l.Contains("First paragraph."));
        firstLine.Should().NotBeNull();
        firstLine.Should().NotContain("Second paragraph.");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithDivTags_ShouldPreserveBlockBreaks()
    {
        // <div> is a block-level element that should also produce line breaks
        var input = @"<div>Block one content.</div><div>Block two content.</div>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // The two divs should be on separate lines
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().BeGreaterThan(1, "Multiple divs should produce multiple lines");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithEmailStructure_ShouldPreserveSections()
    {
        // Simulates the actual structure from the problem email
        var input = @"<p>Hi there,</p>
<p>We have a few queries:</p>
<p><b>Section One:</b></p>
<p>Please provide documents for the following accounts:</p>
<p>Account A<br>Account B<br>Account C</p>
<p><b>Section Two:</b></p>
<p>Please confirm the following details.</p>
<p>Thank you!</p>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Verify structure is preserved
        result.Should().Contain("**Section One:**");
        result.Should().Contain("**Section Two:**");

        // Key: The bold sections should NOT be on the same line as surrounding content
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Find the lines with section headers
        var sectionOneLine = lines.FirstOrDefault(l => l.Contains("**Section One:**"));
        var sectionTwoLine = lines.FirstOrDefault(l => l.Contains("**Section Two:**"));

        sectionOneLine.Should().NotBeNull();
        sectionTwoLine.Should().NotBeNull();

        // Section headers should be separate from the content that follows
        sectionOneLine.Should().NotContain("Please provide documents");
        sectionTwoLine.Should().NotContain("Please confirm");
    }

    #endregion

    #region Standard HTML List Conversion Tests

    [Fact]
    public void ConvertHtmlToMarkdown_WithOrderedList_ConvertsToMarkdownNumberedList()
    {
        // Standard HTML ordered list that should be converted to markdown numbered list
        // Bug: Currently lists are being collapsed into a single line of text
        var input = @"<ol>
<li>First item</li>
<li>Second item</li>
<li>Third item</li>
</ol>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Should produce numbered list with each item on its own line
        result.Should().Contain("1. First item");
        result.Should().Contain("2. Second item");
        result.Should().Contain("3. Third item");

        // Items should be on separate lines
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithUnorderedList_ConvertsToMarkdownBulletList()
    {
        // Standard HTML unordered list that should be converted to markdown bullet list
        var input = @"<ul>
<li>Apple</li>
<li>Banana</li>
<li>Cherry</li>
</ul>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Should produce bullet list with each item on its own line
        result.Should().Contain("- Apple");
        result.Should().Contain("- Banana");
        result.Should().Contain("- Cherry");

        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithOrderedListWithStartAttribute_RespectsStartNumber()
    {
        // Outlook often uses start="2" to continue numbering from previous list
        var input = @"<ol start=""2"">
<li>Second item</li>
<li>Third item</li>
</ol>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Contain("2. Second item");
        result.Should().Contain("3. Third item");
        // Should NOT start with 1
        result.Should().NotContain("1. Second item");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithListFollowingBoldHeading_PreservesBothStructures()
    {
        // Common email pattern: bold heading followed by a list
        var input = @"<p><b>Apolon's Family Trust:</b></p>
<ol>
<li>Please provide a copy of the bank statements.</li>
<li>Please review the attached report.</li>
<li>Please confirm if you still have the account.</li>
</ol>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Bold heading should be preserved
        result.Should().Contain("**Apolon's Family Trust:**");

        // List items should be numbered
        result.Should().Contain("1. Please provide a copy of the bank statements.");
        result.Should().Contain("2. Please review the attached report.");
        result.Should().Contain("3. Please confirm if you still have the account.");

        // Items should be on separate lines
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var line1 = lines.FirstOrDefault(l => l.Contains("1. Please provide"));
        var line2 = lines.FirstOrDefault(l => l.Contains("2. Please review"));
        line1.Should().NotBeNull();
        line2.Should().NotBeNull();
        line1.Should().NotBe(line2);
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithNestedLists_HandlesGracefully()
    {
        // Nested list structure - common in Outlook emails
        var input = @"<ol>
<li>First main item</li>
</ol>
<ul>
<li>Sub-item A</li>
<li>Sub-item B</li>
</ul>
<ol start=""2"">
<li>Second main item</li>
</ol>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Main items should be numbered correctly
        result.Should().Contain("1. First main item");
        result.Should().Contain("2. Second main item");

        // Sub-items should be bullets
        result.Should().Contain("- Sub-item A");
        result.Should().Contain("- Sub-item B");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithListContainingStyledContent_PreservesContent()
    {
        // Outlook wraps list content in span/font tags
        var input = @"<ol>
<li class=""MsoNormal"" style=""color:#201747""><span style=""font-family:Source Sans Pro"">Please confirm the number of kilometres travelled.</span></li>
<li class=""MsoNormal"" style=""color:#201747""><span style=""font-family:Source Sans Pro"">Please confirm if you incurred any donations.</span></li>
</ol>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Contain("1. Please confirm the number of kilometres travelled.");
        result.Should().Contain("2. Please confirm if you incurred any donations.");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithMultipleSeparateLists_ConvertsEachIndependently()
    {
        // Multiple separate lists in an email (like different sections)
        var input = @"<p><b>Section One:</b></p>
<ol>
<li>Task A</li>
<li>Task B</li>
</ol>
<p><b>Section Two:</b></p>
<ol>
<li>Task C</li>
<li>Task D</li>
</ol>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Both lists should start numbering from 1
        // First we check that all items exist
        result.Should().Contain("**Section One:**");
        result.Should().Contain("1. Task A");
        result.Should().Contain("2. Task B");
        result.Should().Contain("**Section Two:**");
        result.Should().Contain("1. Task C");
        result.Should().Contain("2. Task D");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithListItemContainingMultipleSentences_PreservesAllContent()
    {
        // List items often contain multiple sentences
        var input = @"<ol>
<li>Please provide a copy of the bank statements showing the closing balance at 30 June 2024. We need this for the financial year reconciliation.</li>
<li>Please review the attached report. If you have received the payments, let us know so we can adjust.</li>
</ol>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Contain("1. Please provide a copy of the bank statements showing the closing balance at 30 June 2024. We need this for the financial year reconciliation.");
        result.Should().Contain("2. Please review the attached report. If you have received the payments, let us know so we can adjust.");
    }

    #endregion

    #region Underline Tag Conversion Tests

    [Theory]
    [InlineData("<u>underlined text</u>", "<u>underlined text</u>")]
    [InlineData("<U>UPPERCASE TAG</U>", "<u>UPPERCASE TAG</u>")]
    [InlineData("<u>Unit 7/45 Central Walk, Joondalup WA 6027:</u>", "<u>Unit 7/45 Central Walk, Joondalup WA 6027:</u>")]
    public void ConvertHtmlToMarkdown_WithUnderlineTag_PreservesUnderlineAsHtml(string input, string expected)
    {
        // Markdown doesn't have native underline syntax, so we preserve <u> tags as inline HTML
        // which is valid in GitHub-flavored markdown and most markdown renderers
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithUnderlineInParagraph_PreservesUnderlineInContext()
    {
        // From the actual email - underlined property address as a section header
        var input = @"<p style=""text-indent:18.0pt""><u><span style=""font-family:Source Sans Pro,sans-serif;color:#201747"">Unit 7/45 Central Walk, Joondalup WA 6027:</span></u></p>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // The underlined text should be preserved
        result.Should().Contain("<u>Unit 7/45 Central Walk, Joondalup WA 6027:</u>");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithUnderlineContainingNestedTags_PreservesUnderline()
    {
        // Underline tag wrapping styled span (common in Outlook)
        var input = @"<u><span style=""font-family:Arial"">45 Central Walk, Joondalup WA 6027:</span></u>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Should preserve underline around the text content
        result.Should().Be("<u>45 Central Walk, Joondalup WA 6027:</u>");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithMultipleUnderlinedSections_PreservesAll()
    {
        var input = @"<p><b>Section A:</b></p>
<p><u>Property One:</u></p>
<p>Details here.</p>
<p><u>Property Two:</u></p>
<p>More details.</p>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().Contain("<u>Property One:</u>");
        result.Should().Contain("<u>Property Two:</u>");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithEmptyUnderlineTag_RemovesIt()
    {
        // Empty underline tags should be removed like empty bold tags
        var input = @"<u></u>Some text";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        result.Should().NotContain("<u></u>");
        result.Should().Contain("Some text");
    }

    #endregion

    #region Nested List Indentation Tests

    [Fact]
    public void ConvertHtmlToMarkdown_WithBulletListUnderNumberedListItem_IndentsBulletList()
    {
        // From the actual email: A numbered list item followed by a nested bullet list
        // The bullet list items should be indented under the numbered list
        var input = @"<ol>
<li>Please provide bank statements:</li>
</ol>
<ul style=""margin-left:18pt"">
<li>ANZ #6773</li>
<li>ANZ Visa #4176</li>
</ul>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // The bullet list items should be indented to show they're under the numbered list
        // Markdown uses 2-4 spaces for nesting
        result.Should().Contain("1. Please provide bank statements:");
        result.Should().Contain("    - ANZ #6773");
        result.Should().Contain("    - ANZ Visa #4176");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithNestedBulletListInHtml_PreservesContent()
    {
        // True nested list structure (ul inside li) - content is preserved but nesting structure is flattened
        // Note: This is a known limitation. Outlook emails use sibling lists with margin-left instead
        // of true nesting, which IS properly indented. True HTML nesting (ul inside li) would require
        // complex recursive processing.
        var input = @"<ol>
<li>Main item with sub-items:
    <ul>
        <li>Sub-item A</li>
        <li>Sub-item B</li>
    </ul>
</li>
<li>Second main item</li>
</ol>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // All content should be present (nesting structure is flattened)
        result.Should().Contain("Main item with sub-items");
        result.Should().Contain("Sub-item A");
        result.Should().Contain("Sub-item B");
        result.Should().Contain("Second main item");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithOutlookStyleNestedListWithMarginLeft_IndentsItems()
    {
        // Outlook often uses margin-left CSS to indent sublists instead of proper nesting
        // This is the pattern from the actual email
        var input = @"<ol>
<li>Please provide a copy of all Term Deposit account statements. We believe there are the following accounts:</li>
</ol>
<ul style=""margin-top:0cm"" type=""disc"">
<li style=""margin-left:21.25pt"">Term Deposit 2009</li>
<li style=""margin-left:21.25pt"">Term Deposit 7142</li>
<li style=""margin-left:21.25pt"">Term Deposit 8982</li>
<li style=""margin-left:21.25pt"">Term Deposit 5169</li>
</ul>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // The bullet items should be indented to show they belong under the numbered item
        result.Should().Contain("1. Please provide a copy of all Term Deposit account statements");
        result.Should().Contain("    - Term Deposit 2009");
        result.Should().Contain("    - Term Deposit 7142");
        result.Should().Contain("    - Term Deposit 8982");
        result.Should().Contain("    - Term Deposit 5169");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithMultipleSectionsEachWithNestedLists_IndentsEachCorrectly()
    {
        // Email with multiple sections, each having numbered items with sub-bullets
        var input = @"<p><b>Section One:</b></p>
<ol>
<li>Get these bank statements:</li>
</ol>
<ul style=""margin-left:18pt"">
<li>Account A</li>
<li>Account B</li>
</ul>
<p><b>Section Two:</b></p>
<ol>
<li>Get these documents:</li>
</ol>
<ul style=""margin-left:18pt"">
<li>Document X</li>
<li>Document Y</li>
</ul>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // Both sections should have properly indented sub-lists
        result.Should().Contain("**Section One:**");
        result.Should().Contain("1. Get these bank statements:");
        result.Should().Contain("    - Account A");
        result.Should().Contain("    - Account B");

        result.Should().Contain("**Section Two:**");
        result.Should().Contain("1. Get these documents:");
        result.Should().Contain("    - Document X");
        result.Should().Contain("    - Document Y");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithDeeplyNestedLists_PreservesContent()
    {
        // Three levels of true HTML nesting - content is preserved but structure is flattened
        // Note: This is a known limitation. Outlook emails use sibling lists with margin-left/CSS
        // instead of true nesting, which IS properly indented.
        var input = @"<ol>
<li>Main item
    <ul>
        <li>Sub-item
            <ul>
                <li>Sub-sub-item A</li>
                <li>Sub-sub-item B</li>
            </ul>
        </li>
    </ul>
</li>
</ol>";

        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);

        // All content should be present (nesting structure is flattened)
        result.Should().Contain("Main item");
        result.Should().Contain("Sub-item");
        result.Should().Contain("Sub-sub-item A");
        result.Should().Contain("Sub-sub-item B");
    }

    #endregion

    #region Pre Tag Conversion Tests

    [Fact]
    public void ConvertHtmlToMarkdown_WithPreTag_ConvertsToFencedCodeBlock()
    {
        var input = "<pre>console.log('hello');</pre>";
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);
        result.Should().Contain("```");
        result.Should().Contain("console.log('hello');");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithPreTagContainingJson_DetectsJsonLanguage()
    {
        var input = @"<pre>{""message"":""error"",""code"":500}</pre>";
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);
        result.Should().StartWith("```json");
        result.Should().Contain(@"{""message"":""error"",""code"":500}");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithPreTagContainingCssSelectors_DetectsCssLanguage()
    {
        var input = "<pre>div.container > span.text { color: red; }</pre>";
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);
        result.Should().StartWith("```css");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithPreTagContainingStackTrace_DetectsJavaScriptLanguage()
    {
        var input = @"<pre>Error: Request failed
    at fetchData (app.js:42:15)
    at main (index.js:10:5)</pre>";
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);
        result.Should().StartWith("```javascript");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithPreTagContainingHtmlEntities_DecodesEntities()
    {
        var input = @"<pre>{&quot;key&quot;:&quot;value&quot;,&quot;count&quot;:5}</pre>";
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);
        // HTML entities should be decoded inside the code block
        result.Should().Contain(@"{""key"":""value"",""count"":5}");
        result.Should().NotContain("&quot;");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithPreTagWithAttributes_ConvertsToCodeBlock()
    {
        var input = @"<pre style=""background:#f4f4f4;font-size:12px"">code here</pre>";
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);
        result.Should().Contain("```");
        result.Should().Contain("code here");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithMultiplePreTags_ConvertsEach()
    {
        var input = @"<p>First:</p><pre>block1</pre><p>Second:</p><pre>block2</pre>";
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);
        result.Should().Contain("block1");
        result.Should().Contain("block2");
        // Should have two code blocks
        var fenceCount = result.Split("```").Length - 1;
        fenceCount.Should().BeGreaterThanOrEqualTo(4); // Opening and closing for each
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithPreTagContainingPlainText_NoLanguageHint()
    {
        var input = "<pre>Just some plain text content</pre>";
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);
        // Should have code fence but no language (or empty language)
        result.Should().Contain("```\n");
        result.Should().Contain("Just some plain text content");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithPreTagContainingHtml_DetectsHtmlLanguage()
    {
        var input = @"<pre>&lt;!DOCTYPE html&gt;&lt;html&gt;&lt;body&gt;content&lt;/body&gt;&lt;/html&gt;</pre>";
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);
        result.Should().StartWith("```html");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithPreTagContainingXml_DetectsXmlLanguage()
    {
        var input = @"<pre>&lt;?xml version=""1.0""?&gt;&lt;root&gt;data&lt;/root&gt;</pre>";
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);
        result.Should().StartWith("```xml");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithPreTagContainingCSharp_DetectsCSharpLanguage()
    {
        var input = @"<pre>using System;
namespace MyApp {
    public class Program { }
}</pre>";
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);
        result.Should().StartWith("```csharp");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithPreTagContainingJava_DetectsJavaLanguage()
    {
        var input = @"<pre>import java.util.List;
public class Main {
    System.out.println(""Hello"");
}</pre>";
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);
        result.Should().StartWith("```java");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithPreTagContainingPython_DetectsPythonLanguage()
    {
        var input = @"<pre>def hello():
    print('Hello World')
</pre>";
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);
        result.Should().StartWith("```python");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithPreTagContainingTypeScript_DetectsTypeScriptLanguage()
    {
        var input = @"<pre>interface User {
    name: string;
    age: number;
}</pre>";
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);
        result.Should().StartWith("```typescript");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithPreTagContainingCpp_DetectsCppLanguage()
    {
        var input = @"<pre>#include &lt;iostream&gt;
int main() {
    std::cout &lt;&lt; ""Hello"";
}</pre>";
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);
        result.Should().StartWith("```cpp");
    }

    [Fact]
    public void ConvertHtmlToMarkdown_WithPreTagContainingC_DetectsCLanguage()
    {
        var input = @"<pre>#include &lt;stdio.h&gt;
int main() {
    printf(""Hello"");
}</pre>";
        var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);
        result.Should().StartWith("```c\n");
    }

    #endregion
}
