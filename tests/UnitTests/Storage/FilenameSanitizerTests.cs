using M365MailMirror.Core.Storage;

namespace M365MailMirror.UnitTests.Storage;

public class FilenameSanitizerTests
{
    #region SanitizeFilename Tests

    [Fact]
    public void SanitizeFilename_ValidFilename_ReturnsUnchanged()
    {
        var result = FilenameSanitizer.SanitizeFilename("Meeting Notes");

        result.Should().Be("Meeting Notes");
    }

    [Fact]
    public void SanitizeFilename_NullInput_ReturnsUnnamed()
    {
        var result = FilenameSanitizer.SanitizeFilename(null!);

        result.Should().Be("unnamed");
    }

    [Fact]
    public void SanitizeFilename_EmptyString_ReturnsUnnamed()
    {
        var result = FilenameSanitizer.SanitizeFilename("");

        result.Should().Be("unnamed");
    }

    [Fact]
    public void SanitizeFilename_WhitespaceOnly_ReturnsUnnamed()
    {
        var result = FilenameSanitizer.SanitizeFilename("   ");

        result.Should().Be("unnamed");
    }

    [Theory]
    [InlineData("?")]
    [InlineData("*")]
    [InlineData(":")]
    [InlineData("\"")]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("|")]
    [InlineData("/")]
    [InlineData("\\")]
    public void SanitizeFilename_IllegalCharacter_ReplacesWithUnderscore(string illegalChar)
    {
        var input = $"File{illegalChar}Name";

        var result = FilenameSanitizer.SanitizeFilename(input);

        result.Should().Be("File_Name");
    }

    [Fact]
    public void SanitizeFilename_MultipleIllegalCharacters_ReplacesAll()
    {
        var result = FilenameSanitizer.SanitizeFilename("Re: Project Status?");

        result.Should().Be("Re_ Project Status_");
    }

    [Fact]
    public void SanitizeFilename_TrailingDots_Trimmed()
    {
        var result = FilenameSanitizer.SanitizeFilename("filename...");

        result.Should().Be("filename");
    }

    [Fact]
    public void SanitizeFilename_TrailingSpaces_Trimmed()
    {
        var result = FilenameSanitizer.SanitizeFilename("filename   ");

        result.Should().Be("filename");
    }

    [Fact]
    public void SanitizeFilename_LeadingDots_Trimmed()
    {
        var result = FilenameSanitizer.SanitizeFilename("...filename");

        result.Should().Be("filename");
    }

    [Fact]
    public void SanitizeFilename_ControlCharacters_Removed()
    {
        var result = FilenameSanitizer.SanitizeFilename("File\x00\x01\x02Name");

        result.Should().Be("FileName");
    }

    [Fact]
    public void SanitizeFilename_TooLong_Truncated()
    {
        var longName = new string('A', 200);

        var result = FilenameSanitizer.SanitizeFilename(longName, maxLength: 50);

        result.Should().HaveLength(50);
        result.Should().Be(new string('A', 50));
    }

    [Fact]
    public void SanitizeFilename_TruncationLeavesTrailingDot_TrimsIt()
    {
        // "Hello World." where truncation happens at the dot
        var input = new string('A', 45) + "....";

        var result = FilenameSanitizer.SanitizeFilename(input, maxLength: 48);

        result.Should().NotEndWith(".");
        result.Should().Be(new string('A', 45));
    }

    [Fact]
    public void SanitizeFilename_UnicodeCharacters_Preserved()
    {
        var result = FilenameSanitizer.SanitizeFilename("会议记录");

        result.Should().Be("会议记录");
    }

    [Fact]
    public void SanitizeFilename_MixedUnicodeAndAscii_HandledCorrectly()
    {
        var result = FilenameSanitizer.SanitizeFilename("Meeting 会议 Notes");

        result.Should().Be("Meeting 会议 Notes");
    }

    [Fact]
    public void SanitizeFilename_OnlyIllegalChars_ReturnsUnnamed()
    {
        var result = FilenameSanitizer.SanitizeFilename("???***");

        // After replacing with underscores: "______", which is not whitespace-only
        result.Should().Be("______");
    }

    #endregion

    #region GenerateEmlFilename Tests

    [Fact]
    public void GenerateEmlFilename_ValidInput_ReturnsCorrectFormat()
    {
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);

        var result = FilenameSanitizer.GenerateEmlFilename("Meeting Notes", receivedTime);

        result.Should().Be("Meeting Notes_1030.eml");
    }

    [Fact]
    public void GenerateEmlFilename_NullSubject_UsesNoSubject()
    {
        var receivedTime = new DateTimeOffset(2024, 1, 15, 14, 15, 0, TimeSpan.Zero);

        var result = FilenameSanitizer.GenerateEmlFilename(null, receivedTime);

        result.Should().Be("No Subject_1415.eml");
    }

    [Fact]
    public void GenerateEmlFilename_MidnightTime_FormatCorrectly()
    {
        var receivedTime = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);

        var result = FilenameSanitizer.GenerateEmlFilename("Test", receivedTime);

        result.Should().Be("Test_0000.eml");
    }

    [Fact]
    public void GenerateEmlFilename_AlmostMidnight_FormatCorrectly()
    {
        var receivedTime = new DateTimeOffset(2024, 1, 15, 23, 59, 0, TimeSpan.Zero);

        var result = FilenameSanitizer.GenerateEmlFilename("Test", receivedTime);

        result.Should().Be("Test_2359.eml");
    }

    [Fact]
    public void GenerateEmlFilename_SubjectWithIllegalChars_Sanitized()
    {
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);

        var result = FilenameSanitizer.GenerateEmlFilename("Re: Project Status?", receivedTime);

        result.Should().Be("Re_ Project Status__1030.eml");
    }

    [Fact]
    public void GenerateEmlFilename_VeryLongSubject_Truncated()
    {
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var longSubject = new string('A', 200);

        var result = FilenameSanitizer.GenerateEmlFilename(longSubject, receivedTime, maxLength: 50);

        result.Should().HaveLength(59); // 50 (subject) + 9 ("_1030.eml")
        result.Should().EndWith("_1030.eml");
    }

    #endregion

    #region GenerateEmlFilenameWithCounter Tests

    [Fact]
    public void GenerateEmlFilenameWithCounter_Counter1_ReturnsCorrectFormat()
    {
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);

        var result = FilenameSanitizer.GenerateEmlFilenameWithCounter("Meeting Notes", receivedTime, 1);

        result.Should().Be("Meeting Notes_1030_1.eml");
    }

    [Fact]
    public void GenerateEmlFilenameWithCounter_HighCounter_ReturnsCorrectFormat()
    {
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);

        var result = FilenameSanitizer.GenerateEmlFilenameWithCounter("Meeting Notes", receivedTime, 999);

        result.Should().Be("Meeting Notes_1030_999.eml");
    }

    #endregion

    #region SanitizeFolderPath Tests

    [Fact]
    public void SanitizeFolderPath_SimpleFolder_ReturnsUnchanged()
    {
        var result = FilenameSanitizer.SanitizeFolderPath("Inbox");

        result.Should().Be("Inbox");
    }

    [Fact]
    public void SanitizeFolderPath_NestedFolders_UsesPlatformSeparator()
    {
        var result = FilenameSanitizer.SanitizeFolderPath("Inbox/Important");

        result.Should().Contain(Path.DirectorySeparatorChar.ToString());
    }

    [Fact]
    public void SanitizeFolderPath_NullInput_ReturnsUnknown()
    {
        var result = FilenameSanitizer.SanitizeFolderPath(null!);

        result.Should().Be("Unknown");
    }

    [Fact]
    public void SanitizeFolderPath_EmptyString_ReturnsUnknown()
    {
        var result = FilenameSanitizer.SanitizeFolderPath("");

        result.Should().Be("Unknown");
    }

    [Fact]
    public void SanitizeFolderPath_FolderWithIllegalChars_Sanitized()
    {
        var result = FilenameSanitizer.SanitizeFolderPath("My Folder: Important?");

        result.Should().Be("My Folder_ Important_");
    }

    [Fact]
    public void SanitizeFolderPath_NestedWithIllegalChars_AllPartsSanitized()
    {
        var result = FilenameSanitizer.SanitizeFolderPath("Inbox?/Sub:Folder");

        var parts = result.Split(Path.DirectorySeparatorChar);
        parts.Should().HaveCount(2);
        parts[0].Should().Be("Inbox_");
        parts[1].Should().Be("Sub_Folder");
    }

    #endregion

    #region CalculateMaxSubjectLength Tests

    [Fact]
    public void CalculateMaxSubjectLength_ShortPath_ReturnsDefaultMax()
    {
        var result = FilenameSanitizer.CalculateMaxSubjectLength(
            basePath: "C:\\Archive",
            folderPath: "Inbox",
            dateSubPath: "2024\\01");

        result.Should().Be(100); // Default max
    }

    [Fact]
    public void CalculateMaxSubjectLength_LongPath_ReturnsReducedLength()
    {
        var longBasePath = "C:\\" + new string('A', 150);

        var result = FilenameSanitizer.CalculateMaxSubjectLength(
            basePath: longBasePath,
            folderPath: "Very Long Folder Name",
            dateSubPath: "2024\\01");

        result.Should().BeLessThan(100);
        result.Should().BeGreaterOrEqualTo(10); // Minimum
    }

    [Fact]
    public void CalculateMaxSubjectLength_VeryLongPath_ReturnsMinimum()
    {
        var veryLongBasePath = "C:\\" + new string('A', 230);

        var result = FilenameSanitizer.CalculateMaxSubjectLength(
            basePath: veryLongBasePath,
            folderPath: "Folder",
            dateSubPath: "2024\\01");

        result.Should().Be(10); // Minimum
    }

    #endregion
}
