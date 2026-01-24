using M365MailMirror.Infrastructure.Transform;

namespace M365MailMirror.UnitTests.Transform;

public class BreadcrumbHelperTests
{
    #region GetMonthName Tests

    [Theory]
    [InlineData(1, "January")]
    [InlineData(2, "February")]
    [InlineData(3, "March")]
    [InlineData(4, "April")]
    [InlineData(5, "May")]
    [InlineData(6, "June")]
    [InlineData(7, "July")]
    [InlineData(8, "August")]
    [InlineData(9, "September")]
    [InlineData(10, "October")]
    [InlineData(11, "November")]
    [InlineData(12, "December")]
    public void GetMonthName_ValidMonth_ReturnsCorrectName(int month, string expected)
    {
        var result = BreadcrumbHelper.GetMonthName(month);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    [InlineData(100)]
    public void GetMonthName_InvalidMonth_ReturnsFormattedNumber(int month)
    {
        var result = BreadcrumbHelper.GetMonthName(month);

        result.Should().Be(month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("01", "January")]
    [InlineData("02", "February")]
    [InlineData("12", "December")]
    public void GetMonthName_StringInput_ReturnsCorrectName(string monthStr, string expected)
    {
        var result = BreadcrumbHelper.GetMonthName(monthStr);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    [InlineData("abc")]
    public void GetMonthName_InvalidString_ReturnsInputUnchanged(string monthStr)
    {
        var result = BreadcrumbHelper.GetMonthName(monthStr);

        result.Should().Be(monthStr);
    }

    #endregion

    #region GenerateHtmlBreadcrumb Tests

    [Fact]
    public void GenerateHtmlBreadcrumb_SimpleEmailPath_GeneratesCorrectHtml()
    {
        var result = BreadcrumbHelper.GenerateHtmlBreadcrumb(
            "transformed/Inbox/2024/01/Meeting_1030.html",
            "Meeting Notes");

        result.Should().Contain("<nav class=\"breadcrumb\">");
        result.Should().Contain("</nav>");
        result.Should().Contain("Archive");
        result.Should().Contain("Inbox");
        result.Should().Contain("2024");
        result.Should().Contain("January"); // Month converted to name
        result.Should().Contain("Meeting Notes");
        result.Should().Contain("<span class=\"current\">Meeting Notes</span>");
    }

    [Fact]
    public void GenerateHtmlBreadcrumb_EmailPath_ContainsCorrectRelativePaths()
    {
        var result = BreadcrumbHelper.GenerateHtmlBreadcrumb(
            "transformed/Inbox/2024/01/Meeting_1030.html",
            "Subject");

        // Path depth: Inbox/2024/01/filename = 4 levels deep, so Archive index is ../../../../index.html
        result.Should().Contain("href=\"../../../index.html\""); // Archive root
        result.Should().Contain("href=\"../../index.html\""); // Inbox folder
        result.Should().Contain("href=\"../index.html\""); // 2024 year
        result.Should().Contain("href=\"index.html\""); // 01 month (current level)
    }

    [Fact]
    public void GenerateHtmlBreadcrumb_NestedSubfolder_IncludesAllSegments()
    {
        var result = BreadcrumbHelper.GenerateHtmlBreadcrumb(
            "transformed/Inbox/Projects/Work/2024/01/Email_1030.html",
            "Subject");

        result.Should().Contain("Archive");
        result.Should().Contain("Inbox");
        result.Should().Contain("Projects");
        result.Should().Contain("Work");
        result.Should().Contain("2024");
        result.Should().Contain("January");
    }

    [Fact]
    public void GenerateHtmlBreadcrumb_SpecialCharactersInSubject_HtmlEncoded()
    {
        var result = BreadcrumbHelper.GenerateHtmlBreadcrumb(
            "transformed/Inbox/2024/01/Email_1030.html",
            "Subject <script>alert('xss')</script>");

        result.Should().NotContain("<script>");
        result.Should().Contain("&lt;script&gt;");
    }

    #endregion

    #region GenerateMarkdownBreadcrumb Tests

    [Fact]
    public void GenerateMarkdownBreadcrumb_SimpleEmailPath_GeneratesCorrectMarkdown()
    {
        var result = BreadcrumbHelper.GenerateMarkdownBreadcrumb(
            "transformed/Inbox/2024/01/Meeting_1030.md",
            "Meeting Notes");

        result.Should().Contain("[Archive]");
        result.Should().Contain("[Inbox]");
        result.Should().Contain("[2024]");
        result.Should().Contain("[January]"); // Month converted to name
        result.Should().Contain("**Meeting Notes**"); // Current item in bold
    }

    [Fact]
    public void GenerateMarkdownBreadcrumb_EmailPath_ContainsMarkdownExtensions()
    {
        var result = BreadcrumbHelper.GenerateMarkdownBreadcrumb(
            "transformed/Inbox/2024/01/Meeting_1030.md",
            "Subject");

        result.Should().Contain(".md)");
        result.Should().NotContain(".html)");
    }

    [Fact]
    public void GenerateMarkdownBreadcrumb_EmailPath_ContainsCorrectRelativePaths()
    {
        var result = BreadcrumbHelper.GenerateMarkdownBreadcrumb(
            "transformed/Inbox/2024/01/Meeting_1030.md",
            "Subject");

        result.Should().Contain("(../../../index.md)"); // Archive root
        result.Should().Contain("(../../index.md)"); // Inbox folder
        result.Should().Contain("(../index.md)"); // 2024 year
        result.Should().Contain("(index.md)"); // 01 month
    }

    [Fact]
    public void GenerateMarkdownBreadcrumb_UsesGreaterThanSeparators()
    {
        var result = BreadcrumbHelper.GenerateMarkdownBreadcrumb(
            "transformed/Inbox/2024/01/Meeting_1030.md",
            "Subject");

        result.Should().Contain(" > ");
    }

    #endregion

    #region GenerateHtmlIndexBreadcrumb Tests

    [Fact]
    public void GenerateHtmlIndexBreadcrumb_RootIndex_GeneratesArchiveOnly()
    {
        var result = BreadcrumbHelper.GenerateHtmlIndexBreadcrumb("transformed/index.html");

        result.Should().Contain("<nav class=\"breadcrumb\">");
        result.Should().Contain("<span class=\"current\">Archive</span>");
    }

    [Fact]
    public void GenerateHtmlIndexBreadcrumb_FolderIndex_GeneratesCorrectBreadcrumb()
    {
        var result = BreadcrumbHelper.GenerateHtmlIndexBreadcrumb("transformed/Inbox/index.html");

        result.Should().Contain("Archive");
        result.Should().Contain("<span class=\"current\">Inbox</span>");
    }

    [Fact]
    public void GenerateHtmlIndexBreadcrumb_MonthIndex_GeneratesFullBreadcrumb()
    {
        var result = BreadcrumbHelper.GenerateHtmlIndexBreadcrumb("transformed/Inbox/2024/01/index.html");

        result.Should().Contain("Archive");
        result.Should().Contain("Inbox");
        result.Should().Contain("2024");
        result.Should().Contain("<span class=\"current\">January</span>"); // Month is current item
    }

    [Fact]
    public void GenerateHtmlIndexBreadcrumb_YearIndex_ConvertsMonthProperly()
    {
        var result = BreadcrumbHelper.GenerateHtmlIndexBreadcrumb("transformed/Inbox/2024/index.html");

        result.Should().Contain("Archive");
        result.Should().Contain("Inbox");
        result.Should().Contain("<span class=\"current\">2024</span>");
    }

    #endregion

    #region GenerateMarkdownIndexBreadcrumb Tests

    [Fact]
    public void GenerateMarkdownIndexBreadcrumb_RootIndex_GeneratesArchiveOnly()
    {
        var result = BreadcrumbHelper.GenerateMarkdownIndexBreadcrumb("transformed/index.md");

        result.Should().Contain("**Archive**");
    }

    [Fact]
    public void GenerateMarkdownIndexBreadcrumb_FolderIndex_GeneratesCorrectBreadcrumb()
    {
        var result = BreadcrumbHelper.GenerateMarkdownIndexBreadcrumb("transformed/Inbox/index.md");

        result.Should().Contain("[Archive]");
        result.Should().Contain("**Inbox**");
    }

    [Fact]
    public void GenerateMarkdownIndexBreadcrumb_MonthIndex_GeneratesFullBreadcrumb()
    {
        var result = BreadcrumbHelper.GenerateMarkdownIndexBreadcrumb("transformed/Inbox/2024/01/index.md");

        result.Should().Contain("[Archive]");
        result.Should().Contain("[Inbox]");
        result.Should().Contain("[2024]");
        result.Should().Contain("**January**"); // Month is current item
    }

    #endregion

    #region Path Normalization Tests

    [Fact]
    public void GenerateHtmlBreadcrumb_BackslashPaths_NormalizedCorrectly()
    {
        var result = BreadcrumbHelper.GenerateHtmlBreadcrumb(
            "transformed\\Inbox\\2024\\01\\Meeting_1030.html",
            "Subject");

        result.Should().Contain("Archive");
        result.Should().Contain("Inbox");
        result.Should().Contain("2024");
        result.Should().Contain("January");
    }

    [Fact]
    public void GenerateMarkdownBreadcrumb_BackslashPaths_NormalizedCorrectly()
    {
        var result = BreadcrumbHelper.GenerateMarkdownBreadcrumb(
            "transformed\\Inbox\\2024\\01\\Meeting_1030.md",
            "Subject");

        result.Should().Contain("[Archive]");
        result.Should().Contain("[Inbox]");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GenerateHtmlBreadcrumb_EmptyPath_HandlesGracefully()
    {
        var result = BreadcrumbHelper.GenerateHtmlBreadcrumb("", "Subject");

        result.Should().Contain("<nav class=\"breadcrumb\">");
        result.Should().Contain("</nav>");
    }

    [Fact]
    public void GenerateHtmlBreadcrumb_SingleLevelPath_HandlesCorrectly()
    {
        var result = BreadcrumbHelper.GenerateHtmlBreadcrumb("transformed/email.html", "Subject");

        result.Should().Contain("Archive");
        result.Should().Contain("Subject");
    }

    [Fact]
    public void GenerateHtmlIndexBreadcrumb_NestedSubfolders_AllLevelsIncluded()
    {
        var result = BreadcrumbHelper.GenerateHtmlIndexBreadcrumb(
            "transformed/Inbox/Projects/Client A/Reports/2024/06/index.html");

        result.Should().Contain("Archive");
        result.Should().Contain("Inbox");
        result.Should().Contain("Projects");
        result.Should().Contain("Client A");
        result.Should().Contain("Reports");
        result.Should().Contain("2024");
        result.Should().Contain("June"); // Month name
    }

    #endregion
}
