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

    #region GenerateFolderPrefix Tests

    [Fact]
    public void GenerateFolderPrefix_SimpleFolder_ReturnsLowercase()
    {
        var result = FilenameSanitizer.GenerateFolderPrefix("Inbox");

        result.Should().Be("inbox");
    }

    [Fact]
    public void GenerateFolderPrefix_NestedFolder_JoinsWithDash()
    {
        var result = FilenameSanitizer.GenerateFolderPrefix("Inbox/Processed");

        result.Should().Be("inbox-processed");
    }

    [Fact]
    public void GenerateFolderPrefix_DeeplyNestedFolder_JoinsAllLevels()
    {
        var result = FilenameSanitizer.GenerateFolderPrefix("Archive/2024/Important");

        result.Should().Be("archive-2024-important");
    }

    [Fact]
    public void GenerateFolderPrefix_FolderWithSpaces_ReplacesWithDash()
    {
        var result = FilenameSanitizer.GenerateFolderPrefix("Sent Items");

        result.Should().Be("sent-items");
    }

    [Fact]
    public void GenerateFolderPrefix_FolderWithIllegalChars_ReplacesWithDash()
    {
        var result = FilenameSanitizer.GenerateFolderPrefix("Inbox: Work?");

        // Consecutive dashes collapsed to single dash, trailing trimmed
        result.Should().Be("inbox-work");
    }

    [Fact]
    public void GenerateFolderPrefix_NullInput_ReturnsUnknown()
    {
        var result = FilenameSanitizer.GenerateFolderPrefix(null!);

        result.Should().Be("unknown");
    }

    [Fact]
    public void GenerateFolderPrefix_EmptyString_ReturnsUnknown()
    {
        var result = FilenameSanitizer.GenerateFolderPrefix("");

        result.Should().Be("unknown");
    }

    [Fact]
    public void GenerateFolderPrefix_VeryLongPath_Truncated()
    {
        var longPath = "Folder/" + string.Join("/", Enumerable.Repeat("Subfolder", 20));

        var result = FilenameSanitizer.GenerateFolderPrefix(longPath, maxLength: 30);

        result.Length.Should().BeLessOrEqualTo(30);
    }

    [Fact]
    public void GenerateFolderPrefix_ConsecutiveSpaces_CollapsedToSingleDash()
    {
        var result = FilenameSanitizer.GenerateFolderPrefix("My   Folder");

        // Multiple consecutive dashes are collapsed to single dash
        result.Should().Be("my-folder");
    }

    [Fact]
    public void GenerateFolderPrefix_DeepHierarchy_KeepsRootAndDeepest()
    {
        // When folder path is too long, keep first (root) and last (deepest) folders
        var result = FilenameSanitizer.GenerateFolderPrefix(
            "Inbox/Daily Reports/Iris Daily Reports",
            maxLength: 30);

        // Should keep "inbox" and "iris-daily-reports", drop "daily-reports"
        result.Should().Be("inbox-iris-daily-reports");
    }

    [Fact]
    public void GenerateFolderPrefix_VeryDeepHierarchy_DropsMiddleFolders()
    {
        // Deeply nested: Inbox/A/B/C/D/Final Folder
        var result = FilenameSanitizer.GenerateFolderPrefix(
            "Inbox/Level One/Level Two/Level Three/Final Destination",
            maxLength: 30);

        // Should keep root and deepest, drop middle ones
        result.Should().StartWith("inbox-");
        result.Should().EndWith("-final-destination");
        result.Length.Should().BeLessOrEqualTo(30);
    }

    [Fact]
    public void GenerateFolderPrefix_TwoFoldersExceedsMax_KeepsBoth()
    {
        // Only two folders but combined they're long
        var result = FilenameSanitizer.GenerateFolderPrefix(
            "Inbox/Very Long Folder Name Here",
            maxLength: 25);

        // Should keep both but truncate the deepest if needed
        result.Should().StartWith("inbox-");
        result.Length.Should().BeLessOrEqualTo(25);
    }

    #endregion

    #region SanitizeFilenameForPrefix Tests

    [Fact]
    public void SanitizeFilenameForPrefix_ValidFilename_ReturnsLowercaseWithDashes()
    {
        var result = FilenameSanitizer.SanitizeFilenameForPrefix("Meeting Notes");

        result.Should().Be("meeting-notes");
    }

    [Fact]
    public void SanitizeFilenameForPrefix_NullInput_ReturnsUnnamed()
    {
        var result = FilenameSanitizer.SanitizeFilenameForPrefix(null!);

        result.Should().Be("unnamed");
    }

    [Fact]
    public void SanitizeFilenameForPrefix_EmptyString_ReturnsUnnamed()
    {
        var result = FilenameSanitizer.SanitizeFilenameForPrefix("");

        result.Should().Be("unnamed");
    }

    [Fact]
    public void SanitizeFilenameForPrefix_IllegalCharacters_ReplacedWithDash()
    {
        var result = FilenameSanitizer.SanitizeFilenameForPrefix("Re: Project Status?");

        // Consecutive dashes collapsed to single dash, trailing trimmed
        result.Should().Be("re-project-status");
    }

    [Fact]
    public void SanitizeFilenameForPrefix_SpaceDashSpace_CollapsedToSingleDash()
    {
        // Common email subject pattern: "Name - Topic - Date"
        var result = FilenameSanitizer.SanitizeFilenameForPrefix("Sharad - IRIS Daily Report - 31st March 2020");

        // " - " becomes "---" then collapsed to "-"
        result.Should().Be("sharad-iris-daily-report-31st-march-2020");
    }

    [Fact]
    public void SanitizeFilenameForPrefix_MultipleConsecutiveDashes_CollapsedToOne()
    {
        var result = FilenameSanitizer.SanitizeFilenameForPrefix("Test---Multiple----Dashes");

        result.Should().Be("test-multiple-dashes");
    }

    [Fact]
    public void SanitizeFilenameForPrefix_VeryLongSubject_Truncated()
    {
        var longSubject = new string('A', 200);

        var result = FilenameSanitizer.SanitizeFilenameForPrefix(longSubject, maxLength: 50);

        result.Should().HaveLength(50);
        result.Should().Be(new string('a', 50)); // lowercase
    }

    [Fact]
    public void SanitizeFilenameForPrefix_UnicodeCharacters_Preserved()
    {
        var result = FilenameSanitizer.SanitizeFilenameForPrefix("会议记录");

        result.Should().Be("会议记录");
    }

    [Fact]
    public void SanitizeFilenameForPrefix_MixedCase_AllLowercase()
    {
        var result = FilenameSanitizer.SanitizeFilenameForPrefix("URGENT Meeting REMINDER");

        result.Should().Be("urgent-meeting-reminder");
    }

    #endregion

    #region GenerateEmlFilenameWithPrefixes Tests

    [Fact]
    public void GenerateEmlFilenameWithPrefixes_ValidInput_CorrectFormat()
    {
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 45, TimeSpan.Zero);

        var result = FilenameSanitizer.GenerateEmlFilenameWithPrefixes(
            "Inbox",
            "Meeting Notes",
            receivedTime);

        result.Should().Be("inbox_2024-01-15-10-30-45_meeting-notes.eml");
    }

    [Fact]
    public void GenerateEmlFilenameWithPrefixes_NestedFolder_CorrectFormat()
    {
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);

        var result = FilenameSanitizer.GenerateEmlFilenameWithPrefixes(
            "Inbox/Processed",
            "Test",
            receivedTime);

        result.Should().Be("inbox-processed_2024-01-15-10-30-00_test.eml");
    }

    [Fact]
    public void GenerateEmlFilenameWithPrefixes_NullSubject_UsesNoSubject()
    {
        var receivedTime = new DateTimeOffset(2024, 1, 15, 14, 15, 0, TimeSpan.Zero);

        var result = FilenameSanitizer.GenerateEmlFilenameWithPrefixes(
            "Inbox",
            null,
            receivedTime);

        result.Should().Be("inbox_2024-01-15-14-15-00_no-subject.eml");
    }

    [Fact]
    public void GenerateEmlFilenameWithPrefixes_SortOrder_FolderThenDate()
    {
        var time1 = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var time2 = new DateTimeOffset(2024, 1, 15, 11, 0, 0, TimeSpan.Zero);

        var inbox1 = FilenameSanitizer.GenerateEmlFilenameWithPrefixes("Inbox", "A", time1);
        var inbox2 = FilenameSanitizer.GenerateEmlFilenameWithPrefixes("Inbox", "B", time2);
        var sent1 = FilenameSanitizer.GenerateEmlFilenameWithPrefixes("Sent Items", "A", time1);

        // Alphabetically: inbox comes before sent
        string.Compare(inbox1, sent1, StringComparison.Ordinal).Should().BeLessThan(0);
        // Within inbox, earlier date comes first
        string.Compare(inbox1, inbox2, StringComparison.Ordinal).Should().BeLessThan(0);
    }

    [Fact]
    public void GenerateEmlFilenameWithPrefixes_MidnightTime_FormatsCorrectly()
    {
        var receivedTime = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);

        var result = FilenameSanitizer.GenerateEmlFilenameWithPrefixes(
            "Inbox",
            "Test",
            receivedTime);

        result.Should().Be("inbox_2024-01-15-00-00-00_test.eml");
    }

    [Fact]
    public void GenerateEmlFilenameWithPrefixes_VeryLongSubject_Truncated()
    {
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var longSubject = new string('A', 200);

        var result = FilenameSanitizer.GenerateEmlFilenameWithPrefixes(
            "Inbox",
            longSubject,
            receivedTime,
            maxSubjectLength: 50);

        // folder (5) + _ (1) + datetime (19) + _ (1) + subject (50) + .eml (4) = 80
        result.Should().HaveLength(80);
        result.Should().StartWith("inbox_2024-01-15-10-30-00_");
        result.Should().EndWith(".eml");
    }

    #endregion

    #region GenerateEmlFilenameWithPrefixesAndCounter Tests

    [Fact]
    public void GenerateEmlFilenameWithPrefixesAndCounter_Counter1_CorrectFormat()
    {
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 45, TimeSpan.Zero);

        var result = FilenameSanitizer.GenerateEmlFilenameWithPrefixesAndCounter(
            "Inbox",
            "Meeting Notes",
            receivedTime,
            1);

        result.Should().Be("inbox_2024-01-15-10-30-45_meeting-notes_1.eml");
    }

    [Fact]
    public void GenerateEmlFilenameWithPrefixesAndCounter_HighCounter_CorrectFormat()
    {
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);

        var result = FilenameSanitizer.GenerateEmlFilenameWithPrefixesAndCounter(
            "Inbox/Important",
            "Test",
            receivedTime,
            999);

        result.Should().Be("inbox-important_2024-01-15-10-30-00_test_999.eml");
    }

    #endregion

    #region CalculateMaxSubjectLength Tests

    [Fact]
    public void CalculateMaxSubjectLength_ShortPath_ReturnsDefaultMax()
    {
        var result = FilenameSanitizer.CalculateMaxSubjectLength(
            basePath: "C:\\Archive",
            dateSubPath: "2024\\01");

        result.Should().Be(50); // Default max for prefixed format
    }

    [Fact]
    public void CalculateMaxSubjectLength_LongPath_ReturnsReducedLength()
    {
        var longBasePath = "C:\\" + new string('A', 200);

        var result = FilenameSanitizer.CalculateMaxSubjectLength(
            basePath: longBasePath,
            dateSubPath: "2024\\01");

        result.Should().BeLessThan(100);
        result.Should().BeGreaterOrEqualTo(10); // Minimum
    }

    [Fact]
    public void CalculateMaxSubjectLength_VeryLongPath_ReturnsMinimum()
    {
        var veryLongBasePath = "C:\\" + new string('A', 240);

        var result = FilenameSanitizer.CalculateMaxSubjectLength(
            basePath: veryLongBasePath,
            dateSubPath: "2024\\01");

        result.Should().Be(10); // Minimum
    }

    #endregion
}
