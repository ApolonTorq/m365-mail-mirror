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

    // Negative lookbehind (?<!\]\() prevents matching cid refs inside markdown image syntax ![image](cid:...)
    // This preserves converted images while removing standalone unresolved cid references
    private static readonly Regex CidUnbracketedPattern = new(
        @"(?<!\]\()cid:[^\s\[\]<>()]+@[^\s\[\]<>()]+",
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

    // Pattern to collapse single line breaks (with optional leading whitespace) into spaces
    // This handles HTML source code formatting where long lines are wrapped for readability
    // but the wrapping is not semantic (not <br> tags)
    // Handles both Unix (\n) and Windows (\r\n) style line endings
    private static readonly Regex InlineNewlinePattern = new(
        @"(?<!\r?\n)\r?\n[ \t]*(?!\r?\n)",
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

    // HTML to Markdown conversion patterns
    private static readonly Regex BoldTagPattern = new(
        @"<(b|strong)>(.*?)</\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex ItalicTagPattern = new(
        @"<(i|em)>(.*?)</\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
        RegexTimeout);

    // Underline tag pattern - preserved as inline HTML since Markdown has no native underline
    private static readonly Regex UnderlineTagPattern = new(
        @"<u>(.*?)</u>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex ImageTagPattern = new(
        @"<img\s+[^>]*src\s*=\s*[""']([^""']+)[""'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    // Office XML tags like <o:p>, <st1:date>, etc. that should be stripped
    private static readonly Regex OfficeXmlTagPattern = new(
        @"</?(?:o|st\d*):[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    // Pattern to separate consecutive asterisks (from adjacent bold tags)
    // e.g., "**text****more**" → "**text** **more**"
    private static readonly Regex ConsecutiveAsterisksPattern = new(
        @"\*{4,}",
        RegexOptions.Compiled,
        RegexTimeout);

    // HR tag pattern - horizontal rule tags with optional attributes and self-closing
    private static readonly Regex HrTagPattern = new(
        @"<hr[^>]*/?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    // BR tag pattern - line break tags with optional self-closing
    private static readonly Regex BrTagPattern = new(
        @"<br\s*/?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    // Closing paragraph tag - marks end of paragraph block
    private static readonly Regex ClosingParagraphTagPattern = new(
        @"</p\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    // Closing div tag - marks end of div block
    private static readonly Regex ClosingDivTagPattern = new(
        @"</div\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    // Patterns to strip content-bearing tags (style, script, head) entirely
    private static readonly Regex StyleTagPattern = new(
        @"<style[^>]*>[\s\S]*?</style>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex ScriptTagPattern = new(
        @"<script[^>]*>[\s\S]*?</script>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex HeadTagPattern = new(
        @"<head[^>]*>[\s\S]*?</head>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    // Pattern to match XML/IE conditional comments like <!--[if !mso]> ... <![endif]-->
    private static readonly Regex ConditionalCommentPattern = new(
        @"<!--\[if[^\]]*\]>[\s\S]*?<!\[endif\]-->",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    // Pattern to match standard HTML comments
    private static readonly Regex HtmlCommentPattern = new(
        @"<!--[\s\S]*?-->",
        RegexOptions.Compiled,
        RegexTimeout);

    // Pattern to match preformatted text blocks: <pre>...</pre>
    private static readonly Regex PreTagPattern = new(
        @"<pre[^>]*>([\s\S]*?)</pre>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    // Pattern to match ordered lists: <ol>...</ol>
    private static readonly Regex OrderedListPattern = new(
        @"<ol[^>]*>(.*?)</ol>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
        RegexTimeout);

    // Pattern to match unordered lists: <ul>...</ul>
    private static readonly Regex UnorderedListPattern = new(
        @"<ul[^>]*>(.*?)</ul>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
        RegexTimeout);

    // Pattern to extract list items: <li>...</li>
    private static readonly Regex ListItemPattern = new(
        @"<li[^>]*>(.*?)</li>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
        RegexTimeout);

    // Pattern to detect margin-left in styles (CSS indentation indicator)
    private static readonly Regex MarginLeftPattern = new(
        @"margin-left\s*:\s*(\d+(?:\.\d+)?)\s*(pt|px|em|cm|mm|in)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    // Pattern to detect ul/ol immediately following another list (Outlook continuation pattern)
    // Captures: preceding </ol> or </ul> (possibly multiple closing tags), optional whitespace, and the following <ul>
    // Outlook often has nested empty <ol> tags that close before the <ul>, e.g. </ol></ol></ol></ol><ul...>
    private static readonly Regex ListContinuationPattern = new(
        @"(?:</ol>\s*)+(<ul[^>]*>)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    // Pattern to extract start attribute from <ol start="N">
    private static readonly Regex OlStartAttributePattern = new(
        @"<ol[^>]*\bstart\s*=\s*[""']?(\d+)[""']?[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
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
    /// Converts HTML content to Markdown, preserving semantic formatting.
    /// Converts bold, italic, images, tables, and lists to Markdown equivalents,
    /// then strips remaining HTML tags.
    /// Includes safety limits to prevent CPU-intensive processing on pathological input.
    /// </summary>
    /// <param name="html">The HTML content to convert</param>
    /// <returns>Markdown-formatted text with HTML removed and entities decoded</returns>
    public static string ConvertHtmlToMarkdown(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        // Safety limit: truncate extremely large content to prevent excessive CPU usage
        var workingContent = TruncateIfNeeded(html);

        try
        {
            string result = workingContent;

            // Strip non-content sections first (style, script, head, comments)
            // These contain CSS/JS/metadata that shouldn't appear in markdown
            result = HeadTagPattern.Replace(result, "");
            result = StyleTagPattern.Replace(result, "");
            result = ScriptTagPattern.Replace(result, "");
            result = ConditionalCommentPattern.Replace(result, "");
            result = HtmlCommentPattern.Replace(result, "");

            // Convert pre tags to fenced code blocks BEFORE whitespace normalization
            // This preserves the preformatted structure of code content
            result = ConvertPreTagsToCodeFences(result);

            // Collapse single line breaks with trailing whitespace into spaces BEFORE semantic conversions
            // This handles HTML source code formatting (line wraps for readability, not <br> tags)
            // Must happen before table/list conversion so markdown output newlines are preserved
            result = InlineNewlinePattern.Replace(result, " ");
            // Collapse multiple consecutive spaces into single space
            result = Regex.Replace(result, @"[ \t]+", " ", RegexOptions.None, RegexTimeout);

            // Convert semantic HTML to Markdown BEFORE stripping tags
            result = ApplySemanticConversions(result);

            // Multi-pass HTML stripping to handle remaining/nested tags
            // Limit iterations to prevent pathological regex behavior
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

            // Restore underline placeholders to actual <u> tags
            // These were inserted during ApplySemanticConversions to survive HTML stripping
            result = result.Replace("{{U_START}}", "<u>");
            result = result.Replace("{{U_END}}", "</u>");

            // Restore code fence placeholders to actual markdown code fences
            // These were inserted during ConvertPreTagsToCodeFences to survive whitespace normalization
            result = RestoreCodeFencePlaceholders(result);

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
    /// Converts semantic HTML elements to their Markdown equivalents.
    /// </summary>
    private static string ApplySemanticConversions(string html)
    {
        string result = html;

        // Note: Pre tag conversion moved to ConvertHtmlToMarkdown (before whitespace normalization)

        // Convert HR tags to Markdown separator BEFORE stripping other tags
        // <hr>, <hr/>, <hr />, <hr tabindex="-1" align="center" ...>
        result = HrTagPattern.Replace(result, "\n\n---\n\n");

        // Convert BR tags to newlines BEFORE stripping other tags
        // <br>, <br/>, <br />
        result = BrTagPattern.Replace(result, "\n");

        // Strip Office XML tags (like <o:p>, <st1:date>) BEFORE bold conversion
        // These often wrap empty content inside bold tags, causing extra asterisks
        result = OfficeXmlTagPattern.Replace(result, "");

        // Convert bold: <b>text</b> or <strong>text</strong> → **text**
        // Use a MatchEvaluator to normalize whitespace inside the bold content
        result = BoldTagPattern.Replace(result, match =>
        {
            var content = match.Groups[2].Value;
            // Strip inner HTML tags first (font, span, etc.) so whitespace normalization works on text
            content = HtmlTagPattern.Replace(content, "");
            // Normalize internal whitespace (collapse multiple spaces/newlines to single space)
            content = Regex.Replace(content, @"\s+", " ", RegexOptions.None, RegexTimeout).Trim();
            // Skip empty bold tags
            if (string.IsNullOrWhiteSpace(content))
                return "";
            return $"**{content}**";
        });

        // Separate consecutive asterisks (from adjacent bold tags) into proper markdown
        result = ConsecutiveAsterisksPattern.Replace(result, "** **");

        // Convert italic: <i>text</i> or <em>text</em> → *text*
        result = ItalicTagPattern.Replace(result, "*$2*");

        // Convert underline: <u>text</u> → placeholder that survives HTML stripping
        // Use {{U}} placeholders since Markdown doesn't have native underline syntax
        // These will be converted back to <u> tags after HTML stripping
        result = UnderlineTagPattern.Replace(result, match =>
        {
            var content = match.Groups[1].Value;
            // Strip inner HTML tags first (font, span, etc.) so we get clean text
            content = HtmlTagPattern.Replace(content, "");
            // Normalize internal whitespace (collapse multiple spaces/newlines to single space)
            content = Regex.Replace(content, @"\s+", " ", RegexOptions.None, RegexTimeout).Trim();
            // Skip empty underline tags
            if (string.IsNullOrWhiteSpace(content))
                return "";
            return $"{{{{U_START}}}}{content}{{{{U_END}}}}";
        });

        // Convert tables to markdown format (regular tables)
        result = ConvertTablesToMarkdown(result);

        // Convert numbered lists (Outlook-style table lists) BEFORE global image conversion
        // This allows ProcessListCellContent to detect image-only paragraphs
        result = ConvertOutlookTableListsToMarkdown(result);

        // Convert standard HTML lists (<ol><li>, <ul><li>) to Markdown
        // Must happen after Outlook table lists to avoid processing placeholder <ol> tags
        // First: Mark <ul> elements that follow <ol> (Outlook continuation pattern) for indentation
        result = MarkListContinuationsForIndentation(result);
        result = ConvertOrderedListsToMarkdown(result);
        result = ConvertUnorderedListsToMarkdown(result);

        // Convert remaining images: <img src="cid:..."> → ![image](cid:...)
        // This handles images not inside Outlook table lists
        // Tracking pixel images are stripped (returns empty string)
        result = ImageTagPattern.Replace(result, match =>
        {
            var src = match.Groups[1].Value;
            if (TrackingPixelDetector.IsTrackingPixel(src))
            {
                return ""; // Strip tracking pixel
            }

            return $"![image]({src})";
        });

        // Convert closing block-level tags to paragraph breaks AFTER table/list conversion
        // (Table/list conversion relies on <p> structure for image-only paragraph detection)
        // </p> and </div> mark the end of content blocks and should produce paragraph breaks
        result = ClosingParagraphTagPattern.Replace(result, "\n\n");
        result = ClosingDivTagPattern.Replace(result, "\n\n");

        return result;
    }

    /// <summary>
    /// Converts simple HTML tables to Markdown table format.
    /// </summary>
    private static string ConvertTablesToMarkdown(string html)
    {
        // Match simple tables without nested structures
        var tablePattern = new Regex(
            @"<table[^>]*>(.*?)</table>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
            RegexTimeout);

        return tablePattern.Replace(html, match =>
        {
            var tableContent = match.Groups[1].Value;

            // Check if this looks like an Outlook-style list table (has <ol> inside)
            if (tableContent.Contains("<ol", StringComparison.OrdinalIgnoreCase))
            {
                // Let ConvertOutlookTableListsToMarkdown handle this
                return match.Value;
            }

            var rowPattern = new Regex(
                @"<tr[^>]*>(.*?)</tr>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
                RegexTimeout);

            var cellPattern = new Regex(
                @"<t[dh][^>]*>(.*?)</t[dh]>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
                RegexTimeout);

            var rows = rowPattern.Matches(tableContent);
            if (rows.Count == 0) return match.Value;

            var markdownRows = new List<string>();
            int? columnCount = null;
            foreach (Match row in rows)
            {
                var cells = cellPattern.Matches(row.Groups[1].Value);
                if (cells.Count == 0) continue;

                var cellTexts = cells.Cast<Match>()
                    .Select(c => CleanCellContent(c.Groups[1].Value))
                    .ToList();

                markdownRows.Add("| " + string.Join(" | ", cellTexts) + " |");

                // After the first row, add the header separator row
                // Markdown tables require |---|---| after the header to render properly
                if (columnCount == null)
                {
                    columnCount = cellTexts.Count;
                    var separators = Enumerable.Repeat("---", columnCount.Value);
                    markdownRows.Add("| " + string.Join(" | ", separators) + " |");
                }
            }

            if (markdownRows.Count == 0) return match.Value;

            // Add blank lines before and after the table for proper markdown separation
            return "\n\n" + string.Join("\n", markdownRows) + "\n\n";
        });
    }

    /// <summary>
    /// Converts Outlook-style numbered lists implemented as tables to Markdown lists.
    /// Outlook often renders numbered lists as tables with the number in the first column.
    /// </summary>
    private static string ConvertOutlookTableListsToMarkdown(string html)
    {
        // Match tables that contain <ol> elements (Outlook list pattern)
        var tablePattern = new Regex(
            @"<table[^>]*>(.*?)</table>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
            RegexTimeout);

        return tablePattern.Replace(html, match =>
        {
            var tableContent = match.Groups[1].Value;

            // Only process if this looks like an Outlook list table
            if (!tableContent.Contains("<ol", StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }

            var rowPattern = new Regex(
                @"<tr[^>]*>(.*?)</tr>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
                RegexTimeout);

            var cellPattern = new Regex(
                @"<td[^>]*>(.*?)</td>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
                RegexTimeout);

            // Pattern to extract start number from <ol start="N">
            var olStartPattern = new Regex(
                @"<ol[^>]*start\s*=\s*[""']?(\d+)[""']?[^>]*>",
                RegexOptions.IgnoreCase | RegexOptions.Compiled,
                RegexTimeout);

            var rows = rowPattern.Matches(tableContent);
            if (rows.Count == 0) return match.Value;

            var listItems = new List<string>();
            int currentNumber = 1;

            foreach (Match row in rows)
            {
                var cells = cellPattern.Matches(row.Groups[1].Value);
                if (cells.Count < 2) continue;

                // First cell contains the <ol> with number, second cell contains the text
                var numberCell = cells[0].Groups[1].Value;
                var textCell = cells[1].Groups[1].Value;

                // Try to extract the start number from the <ol> tag
                var olMatch = olStartPattern.Match(numberCell);
                if (olMatch.Success)
                {
                    currentNumber = int.Parse(olMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                }

                // Process the text cell, separating text from image-only paragraphs
                var (textContent, separateImages) = ProcessListCellContent(textCell);

                if (!string.IsNullOrWhiteSpace(textContent))
                {
                    listItems.Add($"{currentNumber}. {textContent}");
                    currentNumber++;
                }

                // Add separated images after the list item
                foreach (var image in separateImages)
                {
                    listItems.Add("");  // Blank line to end the list context
                    listItems.Add(image);
                }
            }

            if (listItems.Count == 0) return match.Value;

            // Add blank lines before and after the list for proper markdown separation
            return "\n\n" + string.Join("\n", listItems) + "\n\n";
        });
    }

    /// <summary>
    /// Converts standard HTML ordered lists (<ol><li>...</ol>) to Markdown numbered lists.
    /// </summary>
    private static string ConvertOrderedListsToMarkdown(string html)
    {
        return OrderedListPattern.Replace(html, match =>
        {
            var listContent = match.Groups[1].Value;
            var fullMatch = match.Value;

            // Check if this list is inside a table (Outlook-style list) - skip if so
            // These are handled by ConvertOutlookTableListsToMarkdown
            // We detect this by checking if the <ol> has only <li>&nbsp;</li> content (number placeholder)
            var items = ListItemPattern.Matches(listContent);
            if (items.Count == 0) return fullMatch;

            // Check for Outlook-style placeholder lists (single item with only whitespace/nbsp)
            if (items.Count == 1)
            {
                var itemContent = items[0].Groups[1].Value;
                var cleanedContent = HtmlTagPattern.Replace(itemContent, "").Trim();
                cleanedContent = System.Net.WebUtility.HtmlDecode(cleanedContent).Trim();
                if (string.IsNullOrWhiteSpace(cleanedContent))
                {
                    // This is a placeholder <ol><li>&nbsp;</li></ol> used for numbering in tables
                    return fullMatch;
                }
            }

            // Extract start number if present
            int startNumber = 1;
            var startMatch = OlStartAttributePattern.Match(fullMatch);
            if (startMatch.Success)
            {
                int.TryParse(startMatch.Groups[1].Value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out startNumber);
            }

            var markdownItems = new List<string>();
            int currentNumber = startNumber;

            foreach (Match item in items)
            {
                var itemContent = item.Groups[1].Value;
                var cleanedItem = CleanListItemContent(itemContent);
                if (!string.IsNullOrWhiteSpace(cleanedItem))
                {
                    markdownItems.Add($"{currentNumber}. {cleanedItem}");
                    currentNumber++;
                }
            }

            if (markdownItems.Count == 0) return fullMatch;

            return "\n\n" + string.Join("\n", markdownItems) + "\n\n";
        });
    }

    /// <summary>
    /// Marks unordered lists that follow ordered lists (Outlook continuation pattern) for indentation.
    /// Also handles true nesting (<ul> inside <li>) by recursively processing with depth.
    /// </summary>
    private static string MarkListContinuationsForIndentation(string html)
    {
        // Mark <ul> that follows </ol> as needing indentation (Outlook continuation)
        // Replace the <ul> opening tag with a marked version
        // The match.Value includes all closing </ol> tags before the <ul>
        return ListContinuationPattern.Replace(html, match =>
        {
            var fullMatch = match.Value;
            var ulTag = match.Groups[1].Value;
            // Add a marker attribute to indicate this list should be indented
            // Insert the marker just before the closing >
            var insertPos = ulTag.Length - 1;
            var markedUl = ulTag.Insert(insertPos, " data-indent=\"1\"");
            // Preserve all the closing </ol> tags that precede the <ul>
            var closingOlTags = fullMatch.Substring(0, fullMatch.Length - ulTag.Length);
            return closingOlTags + markedUl;
        });
    }

    /// <summary>
    /// Converts standard HTML unordered lists (<ul><li>...</ul>) to Markdown bullet lists.
    /// Supports indentation based on margin-left CSS or continuation markers.
    /// </summary>
    private static string ConvertUnorderedListsToMarkdown(string html)
    {
        // Pattern that captures the ul tag attributes for indentation detection
        var ulWithAttrsPattern = new Regex(
            @"<ul([^>]*)>(.*?)</ul>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
            RegexTimeout);

        return ulWithAttrsPattern.Replace(html, match =>
        {
            var ulAttributes = match.Groups[1].Value;
            var listContent = match.Groups[2].Value;
            var fullMatch = match.Value;

            var items = ListItemPattern.Matches(listContent);
            if (items.Count == 0) return fullMatch;

            // Determine indentation level based on:
            // 1. data-indent marker (from MarkListContinuationsForIndentation)
            // 2. margin-left CSS on the <ul> element
            int indentLevel = 0;

            if (ulAttributes.Contains("data-indent=", StringComparison.OrdinalIgnoreCase))
            {
                // Extract indent level from marker
                var indentMatch = Regex.Match(ulAttributes, @"data-indent\s*=\s*""?(\d+)""?",
                    RegexOptions.IgnoreCase, RegexTimeout);
                if (indentMatch.Success)
                {
                    indentLevel = int.Parse(indentMatch.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            else if (MarginLeftPattern.IsMatch(ulAttributes))
            {
                // margin-left on <ul> indicates indentation
                indentLevel = 1;
            }

            // Also check first item's margin-left (Outlook often puts it on <li> instead)
            if (indentLevel == 0 && items.Count > 0)
            {
                var firstItemMatch = items[0];
                var liTag = fullMatch.Substring(
                    fullMatch.IndexOf("<li", StringComparison.OrdinalIgnoreCase),
                    Math.Min(100, fullMatch.Length - fullMatch.IndexOf("<li", StringComparison.OrdinalIgnoreCase)));
                if (MarginLeftPattern.IsMatch(liTag))
                {
                    indentLevel = 1;
                }
            }

            var indent = new string(' ', indentLevel * 4);
            var markdownItems = new List<string>();

            foreach (Match item in items)
            {
                var itemContent = item.Groups[1].Value;
                var cleanedItem = CleanListItemContent(itemContent);
                if (!string.IsNullOrWhiteSpace(cleanedItem))
                {
                    markdownItems.Add($"{indent}- {cleanedItem}");
                }
            }

            if (markdownItems.Count == 0) return fullMatch;

            // For indented lists, don't add extra blank lines before
            // since they should flow right after the parent list item
            if (indentLevel > 0)
            {
                return "\n" + string.Join("\n", markdownItems) + "\n";
            }

            return "\n\n" + string.Join("\n", markdownItems) + "\n\n";
        });
    }

    /// <summary>
    /// Cleans list item content by stripping HTML tags, decoding entities, and normalizing whitespace.
    /// </summary>
    private static string CleanListItemContent(string itemHtml)
    {
        // Strip HTML tags from item content
        var stripped = HtmlTagPattern.Replace(itemHtml, "");
        // Decode HTML entities
        stripped = System.Net.WebUtility.HtmlDecode(stripped);
        // Normalize whitespace (collapse multiple spaces/newlines to single space)
        stripped = Regex.Replace(stripped, @"\s+", " ", RegexOptions.None, RegexTimeout);
        return stripped.Trim();
    }

    /// <summary>
    /// Processes list cell content, extracting text and separating image-only paragraphs.
    /// Images in their own paragraphs are returned separately to be placed after the list item.
    /// Images inline with text are kept together.
    /// </summary>
    private static (string TextContent, List<string> SeparateImages) ProcessListCellContent(string cellHtml)
    {
        var separateImages = new List<string>();

        // Pattern to match paragraphs - captures content between <p> tags
        var paragraphPattern = new Regex(
            @"<p[^>]*>(.*?)</p>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
            RegexTimeout);

        // Pattern to strip all tags except <img> - used to detect image-only paragraphs
        // Real emails often wrap images in <font><span> styling tags
        var nonImgTagPattern = new Regex(
            @"<(?!/?(img)\b)[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            RegexTimeout);

        // Pattern to detect if a paragraph (after stripping non-img tags) contains only images
        var imageOnlyPattern = new Regex(
            @"^\s*(<img\s[^>]*>|\s|&nbsp;)+\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
            RegexTimeout);

        // First convert all images to markdown syntax
        var processedHtml = ImageTagPattern.Replace(cellHtml, "![image]($1)");

        // Process paragraphs - extract image-only ones
        var textParts = new List<string>();

        foreach (Match paragraphMatch in paragraphPattern.Matches(cellHtml))
        {
            var paragraphContent = paragraphMatch.Groups[1].Value;

            // Strip non-img tags to check if this paragraph is image-only
            // This handles Outlook's habit of wrapping images in <font><span> tags
            var strippedContent = nonImgTagPattern.Replace(paragraphContent, "");

            // Check if this paragraph is image-only
            if (imageOnlyPattern.IsMatch(strippedContent))
            {
                // Extract the image markdown from this paragraph
                var imageMatch = ImageTagPattern.Match(paragraphContent);
                if (imageMatch.Success)
                {
                    separateImages.Add($"![image]({imageMatch.Groups[1].Value})");
                }
            }
            else
            {
                // This paragraph has text content - process it
                // Convert images within it to markdown
                var processedParagraph = ImageTagPattern.Replace(paragraphContent, "![image]($1)");
                var cleanedParagraph = CleanCellContent(processedParagraph);
                if (!string.IsNullOrWhiteSpace(cleanedParagraph))
                {
                    textParts.Add(cleanedParagraph);
                }
            }
        }

        // If no paragraphs found, process the whole cell content
        if (textParts.Count == 0 && separateImages.Count == 0)
        {
            var cleanText = CleanCellContent(processedHtml);
            return (cleanText, separateImages);
        }

        return (string.Join(" ", textParts), separateImages);
    }

    /// <summary>
    /// Cleans cell content by stripping inner HTML tags, trimming whitespace,
    /// and escaping pipe characters that would break markdown table structure.
    /// </summary>
    private static string CleanCellContent(string cellHtml)
    {
        // Strip HTML tags from cell content
        var stripped = HtmlTagPattern.Replace(cellHtml, "");
        // Decode entities and normalize whitespace
        stripped = System.Net.WebUtility.HtmlDecode(stripped);
        // Replace multiple whitespace with single space
        stripped = Regex.Replace(stripped, @"\s+", " ", RegexOptions.None, RegexTimeout);
        // Escape pipe characters to prevent breaking markdown table structure
        // (literal | in content would be interpreted as column delimiter)
        stripped = stripped.Replace("|", "\\|");
        return stripped.Trim();
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
            text = ConvertHtmlToMarkdown(text);
        }

        text = CleanCidReferences(text);
        text = CleanOutlookStyleLinks(text);

        return text;
    }

    /// <summary>
    /// Converts HTML pre tags to Markdown fenced code blocks with language detection.
    /// Uses placeholders that survive whitespace normalization.
    /// </summary>
    private static string ConvertPreTagsToCodeFences(string html)
    {
        return PreTagPattern.Replace(html, match =>
        {
            var content = match.Groups[1].Value;

            // Strip any remaining HTML tags inside the pre
            content = HtmlTagPattern.Replace(content, "");

            // Decode HTML entities
            content = System.Net.WebUtility.HtmlDecode(content);

            // Detect language
            var language = DetectCodeLanguage(content);

            // Use placeholders that will survive whitespace normalization
            // Also escape newlines in the content to preserve preformatted structure
            var escapedContent = content.Trim().Replace("\n", "{{CODE_NEWLINE}}");
            // Format: {{CODE_FENCE_START:language}}content{{CODE_FENCE_END}}
            return $"{{{{CODE_FENCE_START:{language}}}}}{escapedContent}{{{{CODE_FENCE_END}}}}";
        });
    }

    /// <summary>
    /// Restores code fence placeholders to actual markdown fenced code blocks.
    /// </summary>
    private static string RestoreCodeFencePlaceholders(string text)
    {
        // Pattern to match {{CODE_FENCE_START:language}}content{{CODE_FENCE_END}}
        var pattern = new Regex(
            @"\{\{CODE_FENCE_START:([^}]*)\}\}(.*?)\{\{CODE_FENCE_END\}\}",
            RegexOptions.Singleline | RegexOptions.Compiled,
            RegexTimeout);

        return pattern.Replace(text, match =>
        {
            var language = match.Groups[1].Value;
            var content = match.Groups[2].Value;

            // Restore newline placeholders
            content = content.Replace("{{CODE_NEWLINE}}", "\n");

            // Build the actual markdown code fence
            return $"\n\n```{language}\n{content}\n```\n\n";
        });
    }

    /// <summary>
    /// Detects the programming language of code content using deterministic pattern matching.
    /// Returns the language identifier for syntax highlighting, or empty string if unknown.
    /// </summary>
    private static string DetectCodeLanguage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "";

        var trimmed = content.TrimStart();

        // JSON: starts with { or [ and contains "key": pattern
        if ((trimmed.StartsWith('{') || trimmed.StartsWith('[')) &&
            Regex.IsMatch(content, @"""[^""]+"":", RegexOptions.None, RegexTimeout))
        {
            return "json";
        }

        // HTML: starts with < and contains DOCTYPE or common HTML tags
        if (trimmed.StartsWith('<') &&
            (content.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
             Regex.IsMatch(content, @"<(html|head|body|div|span|table|form)\b", RegexOptions.IgnoreCase, RegexTimeout)))
        {
            return "html";
        }

        // XML: starts with <?xml or has xmlns attribute
        if (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("xmlns=", StringComparison.OrdinalIgnoreCase))
        {
            return "xml";
        }

        // Java: import java., package, or Java-specific patterns
        // Check before C# since both can have "public class" but Java has distinct imports
        if (Regex.IsMatch(content, @"\bimport\s+java\.", RegexOptions.None, RegexTimeout) ||
            Regex.IsMatch(content, @"\bpackage\s+[\w.]+;", RegexOptions.None, RegexTimeout) ||
            content.Contains("System.out.println"))
        {
            return "java";
        }

        // C#: using statements, namespace, or C#-specific keywords
        if (Regex.IsMatch(content, @"\busing\s+(System|Microsoft)\b", RegexOptions.None, RegexTimeout) ||
            Regex.IsMatch(content, @"\bnamespace\s+[\w.]+\s*\{", RegexOptions.None, RegexTimeout) ||
            Regex.IsMatch(content, @"\b(public|private|internal)\s+(class|interface|struct|record)\s+\w+", RegexOptions.None, RegexTimeout))
        {
            return "csharp";
        }

        // Python: def, import, from...import, or class with colon
        if (Regex.IsMatch(content, @"^\s*def\s+\w+\s*\(", RegexOptions.Multiline, RegexTimeout) ||
            Regex.IsMatch(content, @"^\s*from\s+\w+\s+import\s+", RegexOptions.Multiline, RegexTimeout) ||
            Regex.IsMatch(content, @"^\s*import\s+\w+", RegexOptions.Multiline, RegexTimeout) ||
            Regex.IsMatch(content, @"^\s*class\s+\w+.*:", RegexOptions.Multiline, RegexTimeout))
        {
            return "python";
        }

        // TypeScript: interface/type declarations with type annotations
        if (Regex.IsMatch(content, @"\b(interface|type)\s+\w+\s*[={]", RegexOptions.None, RegexTimeout) ||
            Regex.IsMatch(content, @":\s*(string|number|boolean|void)\b", RegexOptions.None, RegexTimeout))
        {
            return "typescript";
        }

        // C/C++: #include, int main, std::, nullptr, printf
        if (Regex.IsMatch(content, @"#include\s*[<""]", RegexOptions.None, RegexTimeout))
        {
            // Distinguish C++ from C
            if (content.Contains("std::") || content.Contains("nullptr") ||
                Regex.IsMatch(content, @"\b(cout|cin|endl|vector|string)\b", RegexOptions.None, RegexTimeout))
            {
                return "cpp";
            }
            return "c";
        }

        // CSS: contains selector patterns like .class { or #id { or property: value;
        if (Regex.IsMatch(content, @"[\.\#][\w-]+\s*\{", RegexOptions.None, RegexTimeout) ||
            Regex.IsMatch(content, @"\b[\w-]+\s*:\s*[\w#-]+\s*;", RegexOptions.None, RegexTimeout))
        {
            return "css";
        }

        // JavaScript: stack traces with "at function (file:line)"
        if (Regex.IsMatch(content, @"\bat\s+\w+.*\(.*:\d+:\d+\)", RegexOptions.None, RegexTimeout))
        {
            return "javascript";
        }

        // JavaScript: function keywords
        if (Regex.IsMatch(content, @"\b(function|const|let|var)\s+\w+", RegexOptions.None, RegexTimeout) ||
            content.Contains(" => "))
        {
            return "javascript";
        }

        // No language detected
        return "";
    }
}
