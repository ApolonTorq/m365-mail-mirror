using FluentAssertions;

using M365MailMirror.Core.Graph;
using M365MailMirror.Core.Sync;

namespace M365MailMirror.UnitTests.Sync;

public class FolderGlobMatcherTests
{
    #region Backward Compatibility - Simple Patterns

    [Theory]
    [InlineData("Inbox", "Inbox", true)]
    [InlineData("Inbox", "Inbox/Work", true)]
    [InlineData("Inbox", "Inbox/Work/Projects", true)]
    [InlineData("Inbox", "Sent Items", false)]
    [InlineData("Inbox", "InboxOther", false)] // Not a prefix match without /
    public void SimplePattern_MatchesFolderAndDescendants(string pattern, string path, bool expected)
    {
        var matcher = new FolderGlobMatcher([pattern]);
        matcher.IsMatch(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("Inbox/Errors", "Inbox/Errors", true)]
    [InlineData("Inbox/Errors", "Inbox/Errors/2024", true)]
    [InlineData("Inbox/Errors", "Inbox/Errors/2024/January", true)]
    [InlineData("Inbox/Errors", "Inbox", false)]
    [InlineData("Inbox/Errors", "Inbox/ErrorsOther", false)]
    public void NestedPattern_MatchesPathAndDescendants(string pattern, string path, bool expected)
    {
        var matcher = new FolderGlobMatcher([pattern]);
        matcher.IsMatch(path).Should().Be(expected);
    }

    #endregion

    #region Wildcard Suffix (Inbox/Azure*)

    [Theory]
    [InlineData("Inbox/Azure*", "Inbox/Azure", true)]
    [InlineData("Inbox/Azure*", "Inbox/Azure Devops", true)]
    [InlineData("Inbox/Azure*", "Inbox/Azure Devops Alerts", true)]
    [InlineData("Inbox/Azure*", "Inbox/AzureNotifications", true)]
    [InlineData("Inbox/Azure*", "Inbox/Azur", false)] // Doesn't start with "Azure"
    [InlineData("Inbox/Azure*", "Inbox/NotAzure", false)]
    [InlineData("Inbox/Azure*", "Inbox/Azure Devops/Subfolder", false)] // * doesn't match /
    [InlineData("Inbox/Azure*", "Inbox", false)]
    [InlineData("Inbox/Azure*", "Azure", false)] // Must be under Inbox
    public void WildcardSuffix_MatchesWithinSegment(string pattern, string path, bool expected)
    {
        var matcher = new FolderGlobMatcher([pattern]);
        matcher.IsMatch(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("*Alerts", "Alerts", true)]
    [InlineData("*Alerts", "SystemAlerts", true)]
    [InlineData("*Alerts", "My Alerts", true)]
    [InlineData("*Alerts", "AlertsOther", false)] // Doesn't end with "Alerts"
    public void WildcardPrefix_MatchesWithinSegment(string pattern, string path, bool expected)
    {
        var matcher = new FolderGlobMatcher([pattern]);
        matcher.IsMatch(path).Should().Be(expected);
    }

    #endregion

    #region Single Star for Immediate Children (Robots/*)

    [Theory]
    [InlineData("Robots/*", "Robots", false)] // Not the parent itself
    [InlineData("Robots/*", "Robots/Bot1", true)]
    [InlineData("Robots/*", "Robots/Bot2", true)]
    [InlineData("Robots/*", "Robots/Any Folder", true)]
    [InlineData("Robots/*", "Robots/Bot1/Logs", false)] // Not grandchildren
    [InlineData("Robots/*", "Robots/Bot1/Logs/2024", false)]
    [InlineData("Robots/*", "OtherRobots/Bot1", false)]
    public void SingleStarChildren_MatchesImmediateChildrenOnly(string pattern, string path, bool expected)
    {
        var matcher = new FolderGlobMatcher([pattern]);
        matcher.IsMatch(path).Should().Be(expected);
    }

    #endregion

    #region Double Star for All Descendants (Robots/**)

    [Theory]
    [InlineData("Robots/**", "Robots", false)] // Not the parent itself
    [InlineData("Robots/**", "Robots/Bot1", true)]
    [InlineData("Robots/**", "Robots/Bot2", true)]
    [InlineData("Robots/**", "Robots/Bot1/Logs", true)]
    [InlineData("Robots/**", "Robots/Bot1/Logs/2024", true)]
    [InlineData("Robots/**", "Robots/Bot1/Logs/2024/January", true)]
    [InlineData("Robots/**", "OtherRobots/Bot1", false)]
    public void DoubleStarDescendants_MatchesAllDescendants(string pattern, string path, bool expected)
    {
        var matcher = new FolderGlobMatcher([pattern]);
        matcher.IsMatch(path).Should().Be(expected);
    }

    #endregion

    #region Leading Double Star (**/Old*)

    [Theory]
    [InlineData("**/Old*", "Old", true)]
    [InlineData("**/Old*", "OldMessages", true)]
    [InlineData("**/Old*", "OldStuff", true)]
    [InlineData("**/Old*", "Inbox/Old", true)]
    [InlineData("**/Old*", "Inbox/OldStuff", true)]
    [InlineData("**/Old*", "Archive/2024/OldMessages", true)]
    [InlineData("**/Old*", "Very/Deep/Path/OldArchive", true)]
    [InlineData("**/Old*", "NewMessages", false)]
    [InlineData("**/Old*", "Inbox/New", false)]
    [InlineData("**/Old*", "Inbox/NotOld", false)] // Doesn't start with "Old"
    public void LeadingDoubleStar_MatchesAtAnyLevel(string pattern, string path, bool expected)
    {
        var matcher = new FolderGlobMatcher([pattern]);
        matcher.IsMatch(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("**/Temp", "Temp", true)]
    [InlineData("**/Temp", "Inbox/Temp", true)]
    [InlineData("**/Temp", "Archive/2024/Temp", true)]
    [InlineData("**/Temp", "TempFolder", false)] // Must be exact "Temp"
    [InlineData("**/Temp", "Inbox/TempStuff", false)]
    public void LeadingDoubleStarExact_MatchesExactNameAtAnyLevel(string pattern, string path, bool expected)
    {
        var matcher = new FolderGlobMatcher([pattern]);
        matcher.IsMatch(path).Should().Be(expected);
    }

    #endregion

    #region Case Insensitivity

    [Theory]
    [InlineData("inbox", "Inbox", true)]
    [InlineData("INBOX", "inbox", true)]
    [InlineData("Inbox", "INBOX", true)]
    [InlineData("Inbox/*", "inbox/Work", true)]
    [InlineData("inbox/work", "Inbox/Work", true)]
    [InlineData("**/old*", "Archive/OldStuff", true)]
    [InlineData("ROBOTS/**", "robots/bot1", true)]
    public void CaseInsensitive_MatchingWorks(string pattern, string path, bool expected)
    {
        var matcher = new FolderGlobMatcher([pattern]);
        matcher.IsMatch(path).Should().Be(expected);
    }

    #endregion

    #region Multiple Patterns

    [Fact]
    public void MultiplePatterns_MatchesAnyPattern()
    {
        var matcher = new FolderGlobMatcher(["Junk Email", "Deleted Items", "**/Temp"]);

        matcher.IsMatch("Junk Email").Should().BeTrue();
        matcher.IsMatch("Deleted Items").Should().BeTrue();
        matcher.IsMatch("Temp").Should().BeTrue();
        matcher.IsMatch("Archive/Temp").Should().BeTrue();
        matcher.IsMatch("Inbox").Should().BeFalse();
        matcher.IsMatch("Sent Items").Should().BeFalse();
    }

    [Fact]
    public void MultiplePatterns_ComplexScenario()
    {
        var matcher = new FolderGlobMatcher([
            "Junk Email",
            "Deleted Items",
            "Inbox/Azure Devops*",
            "Robots/*",
            "**/Old*"
        ]);

        // Simple patterns
        matcher.IsMatch("Junk Email").Should().BeTrue();
        matcher.IsMatch("Junk Email/Subfolder").Should().BeTrue();
        matcher.IsMatch("Deleted Items").Should().BeTrue();

        // Wildcard suffix
        matcher.IsMatch("Inbox/Azure Devops").Should().BeTrue();
        matcher.IsMatch("Inbox/Azure Devops Alerts").Should().BeTrue();
        matcher.IsMatch("Inbox/Other").Should().BeFalse();

        // Single star children
        matcher.IsMatch("Robots/Bot1").Should().BeTrue();
        matcher.IsMatch("Robots").Should().BeFalse();
        matcher.IsMatch("Robots/Bot1/Logs").Should().BeFalse();

        // Leading double star
        matcher.IsMatch("OldArchive").Should().BeTrue();
        matcher.IsMatch("Archive/OldStuff").Should().BeTrue();

        // Non-matching
        matcher.IsMatch("Inbox").Should().BeFalse();
        matcher.IsMatch("Sent Items").Should().BeFalse();
    }

    #endregion

    #region FilterFolders Integration

    [Fact]
    public void FilterFolders_RemovesMatchingFolders()
    {
        var folders = new List<AppMailFolder>
        {
            CreateFolder("1", "Inbox", "Inbox"),
            CreateFolder("2", "Work", "Inbox/Work"),
            CreateFolder("3", "Junk Email", "Junk Email"),
            CreateFolder("4", "Archive", "Archive"),
            CreateFolder("5", "OldStuff", "Archive/OldStuff")
        };

        var matcher = new FolderGlobMatcher(["Junk Email", "**/Old*"]);
        var filtered = matcher.FilterFolders(folders);

        filtered.Should().HaveCount(3);
        filtered.Should().Contain(f => f.DisplayName == "Inbox");
        filtered.Should().Contain(f => f.DisplayName == "Work");
        filtered.Should().Contain(f => f.DisplayName == "Archive");
        filtered.Should().NotContain(f => f.DisplayName == "Junk Email");
        filtered.Should().NotContain(f => f.DisplayName == "OldStuff");
    }

    [Fact]
    public void FilterFolders_WithNoPatterns_ReturnsAllFolders()
    {
        var folders = new List<AppMailFolder>
        {
            CreateFolder("1", "Inbox", "Inbox"),
            CreateFolder("2", "Junk Email", "Junk Email"),
            CreateFolder("3", "Archive", "Archive")
        };

        var matcher = new FolderGlobMatcher([]);
        var filtered = matcher.FilterFolders(folders);

        filtered.Should().HaveCount(3);
    }

    [Fact]
    public void FilterFolders_WithEmptyFolderList_ReturnsEmpty()
    {
        var matcher = new FolderGlobMatcher(["Junk Email"]);
        var filtered = matcher.FilterFolders([]);

        filtered.Should().BeEmpty();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void EmptyPattern_IsIgnored()
    {
        var matcher = new FolderGlobMatcher(["", "  ", "Inbox"]);
        matcher.IsMatch("Inbox").Should().BeTrue();
        matcher.IsMatch("").Should().BeFalse();
    }

    [Fact]
    public void NullPath_ReturnsFalse()
    {
        var matcher = new FolderGlobMatcher(["Inbox"]);
        matcher.IsMatch(null!).Should().BeFalse();
    }

    [Fact]
    public void EmptyPath_ReturnsFalse()
    {
        var matcher = new FolderGlobMatcher(["Inbox"]);
        matcher.IsMatch("").Should().BeFalse();
    }

    [Theory]
    [InlineData("Inbox (Old)", "Inbox (Old)", true)] // Regex special chars
    [InlineData("Archive [2024]", "Archive [2024]", true)]
    [InlineData("Test.Folder", "Test.Folder", true)]
    [InlineData("Folder+Plus", "Folder+Plus", true)]
    [InlineData("Folder$Dollar", "Folder$Dollar", true)]
    [InlineData("Folder^Caret", "Folder^Caret", true)]
    public void SpecialRegexChars_AreEscapedProperly(string pattern, string path, bool expected)
    {
        var matcher = new FolderGlobMatcher([pattern]);
        matcher.IsMatch(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("*/Work", "Inbox/Work", true)]
    [InlineData("*/Work", "Archive/Work", true)]
    [InlineData("*/Work", "Work", false)] // Must have parent
    [InlineData("*/Work", "Inbox/Other/Work", false)] // Only one level deep
    public void MiddleSingleStar_MatchesSingleSegment(string pattern, string path, bool expected)
    {
        var matcher = new FolderGlobMatcher([pattern]);
        matcher.IsMatch(path).Should().Be(expected);
    }

    #endregion

    #region Real-World Scenarios

    [Fact]
    public void RealWorldScenario_CommonExclusions()
    {
        var matcher = new FolderGlobMatcher([
            "Junk Email",
            "Deleted Items",
            "Drafts",
            "Outbox",
            "Archive",
            "Notes",
            "Snoozed",
            "Conversation History",
            "Recoverable Items",
            "RSS Feeds",
            "Search Folders",
            "Inbox/Azure Devops*",
            "Inbox/Unimportant*",
            "Inbox/Errors",
            "Robots/*"
        ]);

        // Should be excluded
        matcher.IsMatch("Junk Email").Should().BeTrue();
        matcher.IsMatch("Deleted Items").Should().BeTrue();
        matcher.IsMatch("Drafts").Should().BeTrue();
        matcher.IsMatch("Archive").Should().BeTrue();
        matcher.IsMatch("Archive/2024").Should().BeTrue();
        matcher.IsMatch("Inbox/Azure Devops").Should().BeTrue();
        matcher.IsMatch("Inbox/Azure Devops Alerts").Should().BeTrue();
        matcher.IsMatch("Inbox/UnimportantStuff").Should().BeTrue();
        matcher.IsMatch("Inbox/Errors").Should().BeTrue();
        matcher.IsMatch("Inbox/Errors/2024").Should().BeTrue();
        matcher.IsMatch("Robots/Bot1").Should().BeTrue();

        // Should NOT be excluded
        matcher.IsMatch("Inbox").Should().BeFalse();
        matcher.IsMatch("Inbox/Work").Should().BeFalse();
        matcher.IsMatch("Inbox/Important").Should().BeFalse();
        matcher.IsMatch("Sent Items").Should().BeFalse();
        matcher.IsMatch("Robots").Should().BeFalse(); // Parent not matched by /*
        matcher.IsMatch("Robots/Bot1/Logs").Should().BeFalse(); // Grandchildren not matched
    }

    #endregion

    private static AppMailFolder CreateFolder(string id, string displayName, string fullPath)
    {
        return new AppMailFolder
        {
            Id = id,
            DisplayName = displayName,
            FullPath = fullPath,
            TotalItemCount = 0,
            UnreadItemCount = 0
        };
    }
}
