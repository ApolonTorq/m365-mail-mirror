using M365MailMirror.Core.Storage;

namespace M365MailMirror.UnitTests.Storage;

public class PathNormalizationHelperTests
{
    #region NormalizePath Tests

    [Fact]
    public void NormalizePath_NullInput_ReturnsEmptyString()
    {
        var result = PathNormalizationHelper.NormalizePath(null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void NormalizePath_EmptyString_ReturnsEmptyString()
    {
        var result = PathNormalizationHelper.NormalizePath("");

        result.Should().BeEmpty();
    }

    [Fact]
    public void NormalizePath_AsciiPath_ReturnsUnchanged()
    {
        var path = @"eml\Inbox\2024\01\Meeting Notes_1030.eml";

        var result = PathNormalizationHelper.NormalizePath(path);

        result.Should().Be(path);
    }

    [Fact]
    public void NormalizePath_ChineseCharacters_ReturnsNormalized()
    {
        var path = @"eml\Inbox\2024\01\会议记录_1030.eml";

        var result = PathNormalizationHelper.NormalizePath(path);

        result.Should().Be(path);
    }

    [Fact]
    public void NormalizePath_EmojiCharacters_ReturnsNormalized()
    {
        // Emojis are a common source of normalization issues
        var path = @"eml\Inbox\2024\01\Hello World 🌍_1030.eml";

        var result = PathNormalizationHelper.NormalizePath(path);

        // NFC normalization should not change well-formed emoji paths
        result.Should().Be(path);
    }

    [Fact]
    public void NormalizePath_MultipleEmojis_ReturnsNormalized()
    {
        // Multiple emojis as in the original bug report
        var path = @"eml\Inbox\2019\09\GittaLife is taking a 4 weeks-break 🏖️🌴🏝️🌊_0510.eml";

        var result = PathNormalizationHelper.NormalizePath(path);

        // The path should contain the emojis after normalization
        result.Should().Contain("🏖");
        result.Should().Contain("🌴");
        result.Should().Contain("🏝");
        result.Should().Contain("🌊");
    }

    [Fact]
    public void NormalizePath_CombiningCharacters_NormalizesToNFC()
    {
        // NFD: "é" as "e" + combining acute accent (U+0065 U+0301)
        var nfdPath = "eml\\Inbox\\cafe\u0301_1030.eml";

        var result = PathNormalizationHelper.NormalizePath(nfdPath);

        // NFC: "é" as single character (U+00E9)
        result.Should().Contain("\u00E9");
        result.Should().NotContain("\u0301"); // Combining accent should be gone
    }

    [Fact]
    public void NormalizePath_AlreadyNFC_ReturnsIdentical()
    {
        // NFC: "é" as single character (U+00E9)
        var nfcPath = "eml\\Inbox\\caf\u00E9_1030.eml";

        var result = PathNormalizationHelper.NormalizePath(nfcPath);

        result.Should().Be(nfcPath);
    }

    [Fact]
    public void NormalizePath_MixedNormalization_NormalizesAll()
    {
        // Mix of NFD and regular characters
        var mixedPath = "eml\\Inbox\\Resume\u0301 Meeting_1030.eml"; // "Résumé" with decomposed é

        var result = PathNormalizationHelper.NormalizePath(mixedPath);

        // Should contain composed é
        result.Should().Contain("\u00E9");
    }

    [Fact]
    public void NormalizePath_SurrogatePairs_PreservedCorrectly()
    {
        // Emoji that requires surrogate pair in UTF-16 (U+1F600 = Grinning Face)
        var path = "eml\\Inbox\\Happy 😀 Message_1030.eml";

        var result = PathNormalizationHelper.NormalizePath(path);

        result.Should().Contain("😀");
    }

    [Fact]
    public void NormalizePath_JapaneseCharacters_ReturnsNormalized()
    {
        var path = @"eml\Inbox\2024\01\会議の議事録_1030.eml";

        var result = PathNormalizationHelper.NormalizePath(path);

        result.Should().Be(path);
    }

    [Fact]
    public void NormalizePath_ArabicCharacters_ReturnsNormalized()
    {
        var path = @"eml\Inbox\2024\01\اجتماع المشروع_1030.eml";

        var result = PathNormalizationHelper.NormalizePath(path);

        result.Should().Be(path);
    }

    #endregion

    #region HasPotentialNormalizationIssues Tests

    [Fact]
    public void HasPotentialNormalizationIssues_NullInput_ReturnsFalse()
    {
        var result = PathNormalizationHelper.HasPotentialNormalizationIssues(null);

        result.Should().BeFalse();
    }

    [Fact]
    public void HasPotentialNormalizationIssues_EmptyString_ReturnsFalse()
    {
        var result = PathNormalizationHelper.HasPotentialNormalizationIssues("");

        result.Should().BeFalse();
    }

    [Fact]
    public void HasPotentialNormalizationIssues_AsciiPath_ReturnsFalse()
    {
        var result = PathNormalizationHelper.HasPotentialNormalizationIssues(@"eml\Inbox\Meeting_1030.eml");

        result.Should().BeFalse();
    }

    [Fact]
    public void HasPotentialNormalizationIssues_NFCPath_ReturnsFalse()
    {
        // Already in NFC form
        var result = PathNormalizationHelper.HasPotentialNormalizationIssues("eml\\Inbox\\caf\u00E9_1030.eml");

        result.Should().BeFalse();
    }

    [Fact]
    public void HasPotentialNormalizationIssues_NFDPath_ReturnsTrue()
    {
        // NFD form with combining character
        var result = PathNormalizationHelper.HasPotentialNormalizationIssues("eml\\Inbox\\cafe\u0301_1030.eml");

        result.Should().BeTrue();
    }

    [Fact]
    public void HasPotentialNormalizationIssues_EmojiPath_ReturnsFalse()
    {
        // Well-formed emojis typically don't need normalization
        var result = PathNormalizationHelper.HasPotentialNormalizationIssues("eml\\Inbox\\Hello 🌍_1030.eml");

        result.Should().BeFalse();
    }

    #endregion

    #region GetDiagnosticRepresentation Tests

    [Fact]
    public void GetDiagnosticRepresentation_NullInput_ReturnsEmptyMarker()
    {
        var result = PathNormalizationHelper.GetDiagnosticRepresentation(null);

        result.Should().Be("(empty)");
    }

    [Fact]
    public void GetDiagnosticRepresentation_EmptyString_ReturnsEmptyMarker()
    {
        var result = PathNormalizationHelper.GetDiagnosticRepresentation("");

        result.Should().Be("(empty)");
    }

    [Fact]
    public void GetDiagnosticRepresentation_AsciiString_ReturnsHexBytes()
    {
        var result = PathNormalizationHelper.GetDiagnosticRepresentation("ABC");

        // "ABC" in UTF-8 is 0x41 0x42 0x43
        result.Should().StartWith("UTF8[3]: ");
        result.Should().Contain("41-42-43");
    }

    [Fact]
    public void GetDiagnosticRepresentation_EmojiString_ShowsMultibyteSequence()
    {
        var result = PathNormalizationHelper.GetDiagnosticRepresentation("🌍");

        // 🌍 (U+1F30D) in UTF-8 is 0xF0 0x9F 0x8C 0x8D
        result.Should().StartWith("UTF8[4]: ");
        result.Should().Contain("F0-9F-8C-8D");
    }

    [Fact]
    public void GetDiagnosticRepresentation_AccentedChar_ShowsCorrectBytes()
    {
        // NFC é (U+00E9) in UTF-8 is 0xC3 0xA9
        var result = PathNormalizationHelper.GetDiagnosticRepresentation("\u00E9");

        result.Should().StartWith("UTF8[2]: ");
        result.Should().Contain("C3-A9");
    }

    #endregion

    #region Round-trip Consistency Tests

    [Theory]
    [InlineData(@"eml\Inbox\Simple_1030.eml")]
    [InlineData(@"eml\Inbox\Meeting Notes_1030.eml")]
    [InlineData(@"eml\Inbox\会议记录_1030.eml")]
    [InlineData(@"eml\Inbox\Hello 🌍_1030.eml")]
    [InlineData(@"eml\Inbox\Café Menu_1030.eml")]
    public void NormalizePath_MultipleCalls_ReturnsSameResult(string path)
    {
        // Normalizing multiple times should be idempotent
        var normalized1 = PathNormalizationHelper.NormalizePath(path);
        var normalized2 = PathNormalizationHelper.NormalizePath(normalized1);
        var normalized3 = PathNormalizationHelper.NormalizePath(normalized2);

        normalized1.Should().Be(normalized2);
        normalized2.Should().Be(normalized3);
    }

    [Fact]
    public void NormalizePath_NFDAndNFC_ProduceSameResult()
    {
        // NFD form
        var nfdPath = "eml\\Inbox\\cafe\u0301_1030.eml";
        // NFC form
        var nfcPath = "eml\\Inbox\\caf\u00E9_1030.eml";

        var normalizedNFD = PathNormalizationHelper.NormalizePath(nfdPath);
        var normalizedNFC = PathNormalizationHelper.NormalizePath(nfcPath);

        normalizedNFD.Should().Be(normalizedNFC);
    }

    #endregion
}
