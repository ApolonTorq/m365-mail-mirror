using M365MailMirror.Core.Database.Entities;
using M365MailMirror.Infrastructure.Transform;

namespace M365MailMirror.UnitTests.Transform;

/// <summary>
/// Unit tests for CidRewriteHelper which handles rewriting cid: references
/// to point to extracted attachment files.
/// </summary>
public class CidRewriteHelperTests
{
    // Helper to create minimal valid Attachment for testing
    private static Attachment CreateAttachment(
        string messageId,
        string filename,
        string? filePath,
        string? contentId,
        bool isInline,
        bool skipped = false,
        string? skipReason = null)
    {
        return new Attachment
        {
            MessageId = messageId,
            Filename = filename,
            FilePath = filePath,
            ContentId = contentId,
            IsInline = isInline,
            Skipped = skipped,
            SkipReason = skipReason,
            SizeBytes = 1024, // Required field
            ExtractedAt = DateTimeOffset.UtcNow // Required field
        };
    }

    #region BuildCidToPathMapping Tests

    [Fact]
    public void BuildCidToPathMapping_WithInlineAttachment_IncludesInMapping()
    {
        // Arrange - inline attachment with ContentId
        var attachments = new List<Attachment>
        {
            CreateAttachment(
                messageId: "msg1",
                filename: "image001.jpg",
                filePath: "transformed/Inbox/2024/01/images/email_1.jpg",
                contentId: "image001@test.local",
                isInline: true)
        };

        // Act
        var mapping = CidRewriteHelper.BuildCidToPathMapping(
            "transformed/Inbox/2024/01/email.md",
            attachments);

        // Assert
        mapping.Should().ContainKey("image001@test.local");
    }

    /// <summary>
    /// This test demonstrates the bug fix: attachments with Content-Disposition: attachment
    /// (IsInline = false) that have a Content-ID should be included in the CID mapping
    /// because some email clients (like Outlook) reference them via cid: in the HTML body.
    /// </summary>
    [Fact]
    public void BuildCidToPathMapping_WithAttachmentDispositionButContentId_IncludesInMapping()
    {
        // Arrange - attachment with Content-Disposition: attachment but has ContentId
        // This is the exact scenario from the bug report where Outlook marks the image
        // as "attachment" but still uses cid: reference in the HTML body
        var attachments = new List<Attachment>
        {
            CreateAttachment(
                messageId: "msg1",
                filename: "image001.jpg",
                filePath: "transformed/Inbox/2006/01/attachments/email_attachments/image001.jpg",
                contentId: "421002300@31012006-05B3", // Real CID from the bug report
                isInline: false) // Content-Disposition: attachment
        };

        // Act
        var mapping = CidRewriteHelper.BuildCidToPathMapping(
            "transformed/Inbox/2006/01/email.md",
            attachments);

        // Assert - the attachment should be in the mapping despite IsInline = false
        mapping.Should().ContainKey("421002300@31012006-05B3");
    }

    [Fact]
    public void BuildCidToPathMapping_WithSkippedAttachment_ExcludesFromMapping()
    {
        // Arrange - skipped attachment should not be in mapping
        var attachments = new List<Attachment>
        {
            CreateAttachment(
                messageId: "msg1",
                filename: "malware.exe",
                filePath: null,
                contentId: "exe123@test.local",
                isInline: false,
                skipped: true,
                skipReason: "executable:exe")
        };

        // Act
        var mapping = CidRewriteHelper.BuildCidToPathMapping(
            "transformed/Inbox/2024/01/email.md",
            attachments);

        // Assert
        mapping.Should().NotContainKey("exe123@test.local");
    }

    [Fact]
    public void BuildCidToPathMapping_WithNullFilePath_ExcludesFromMapping()
    {
        // Arrange - attachment without file path
        var attachments = new List<Attachment>
        {
            CreateAttachment(
                messageId: "msg1",
                filename: "image001.jpg",
                filePath: null,
                contentId: "image001@test.local",
                isInline: true)
        };

        // Act
        var mapping = CidRewriteHelper.BuildCidToPathMapping(
            "transformed/Inbox/2024/01/email.md",
            attachments);

        // Assert
        mapping.Should().NotContainKey("image001@test.local");
    }

    [Fact]
    public void BuildCidToPathMapping_WithNullContentId_ExcludesFromMapping()
    {
        // Arrange - attachment without ContentId
        var attachments = new List<Attachment>
        {
            CreateAttachment(
                messageId: "msg1",
                filename: "document.pdf",
                filePath: "transformed/Inbox/2024/01/attachments/email_attachments/document.pdf",
                contentId: null,
                isInline: false)
        };

        // Act
        var mapping = CidRewriteHelper.BuildCidToPathMapping(
            "transformed/Inbox/2024/01/email.md",
            attachments);

        // Assert
        mapping.Should().BeEmpty();
    }

    #endregion

    #region RewriteCidReferencesMarkdown Tests

    [Fact]
    public void RewriteCidReferencesMarkdown_WithMatchingInlineAttachment_RewritesCidReference()
    {
        // Arrange
        var markdown = "Some text ![image](cid:image001@test.local) more text";
        var attachments = new List<Attachment>
        {
            CreateAttachment(
                messageId: "msg1",
                filename: "image001.jpg",
                filePath: "transformed/Inbox/2024/01/images/email_1.jpg",
                contentId: "image001@test.local",
                isInline: true)
        };

        // Act
        var result = CidRewriteHelper.RewriteCidReferencesMarkdown(
            markdown,
            "transformed/Inbox/2024/01/email.md",
            attachments);

        // Assert
        result.Should().Contain("![image](images/email_1.jpg)");
        result.Should().NotContain("cid:");
    }

    /// <summary>
    /// This test demonstrates the fix for the bug: CID references should be resolved
    /// even when the attachment has Content-Disposition: attachment (IsInline = false).
    /// </summary>
    [Fact]
    public void RewriteCidReferencesMarkdown_WithAttachmentDispositionButContentId_RewritesCidReference()
    {
        // Arrange - this simulates the exact bug scenario from the issue
        // The email has Content-Disposition: attachment but also Content-ID
        // and the HTML references the image via cid:
        var markdown = "Spent time with a tow truck icon. ![image](cid:421002300@31012006-05B3)";
        var attachments = new List<Attachment>
        {
            CreateAttachment(
                messageId: "msg1",
                filename: "image001.jpg",
                filePath: "transformed/Inbox/Daily Reports/DailyReports.Iris/2006/01/attachments/RE_ Iris Daily Progress Report - 30th January 2005_0023_attachments/image001.jpg",
                contentId: "421002300@31012006-05B3",
                isInline: false) // Content-Disposition: attachment
        };

        // Act
        var result = CidRewriteHelper.RewriteCidReferencesMarkdown(
            markdown,
            "transformed/Inbox/Daily Reports/DailyReports.Iris/2006/01/RE_ Iris Daily Progress Report - 30th January 2005_0023.md",
            attachments);

        // Assert - the CID reference should be resolved to the actual image path
        result.Should().Contain("![image](attachments/RE_ Iris Daily Progress Report - 30th January 2005_0023_attachments/image001.jpg)");
        result.Should().NotContain("cid:");
    }

    [Fact]
    public void RewriteCidReferencesMarkdown_WithNoMatchingAttachment_LeavesCidReference()
    {
        // Arrange - CID reference with no matching attachment
        var markdown = "Some text ![image](cid:unknown@test.local) more text";
        var attachments = new List<Attachment>();

        // Act
        var result = CidRewriteHelper.RewriteCidReferencesMarkdown(
            markdown,
            "transformed/Inbox/2024/01/email.md",
            attachments);

        // Assert - should leave the original reference unchanged
        result.Should().Contain("![image](cid:unknown@test.local)");
    }

    [Fact]
    public void RewriteCidReferencesMarkdown_WithNullInput_ReturnsNull()
    {
        // Act
        var result = CidRewriteHelper.RewriteCidReferencesMarkdown(
            null!,
            "transformed/Inbox/2024/01/email.md",
            new List<Attachment>());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void RewriteCidReferencesMarkdown_WithEmptyInput_ReturnsEmpty()
    {
        // Act
        var result = CidRewriteHelper.RewriteCidReferencesMarkdown(
            "",
            "transformed/Inbox/2024/01/email.md",
            new List<Attachment>());

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region RewriteCidReferencesHtml Tests

    [Fact]
    public void RewriteCidReferencesHtml_WithMatchingAttachment_RewritesCidReference()
    {
        // Arrange
        var html = "<p>Text <img src=\"cid:image001@test.local\" width=\"100\"> more</p>";
        var attachments = new List<Attachment>
        {
            CreateAttachment(
                messageId: "msg1",
                filename: "image001.jpg",
                filePath: "transformed/Inbox/2024/01/images/email_1.jpg",
                contentId: "image001@test.local",
                isInline: true)
        };

        // Act
        var result = CidRewriteHelper.RewriteCidReferencesHtml(
            html,
            "transformed/Inbox/2024/01/email.html",
            attachments);

        // Assert
        result.Should().Contain("src=\"images/email_1.jpg\"");
        result.Should().NotContain("cid:");
    }

    [Fact]
    public void RewriteCidReferencesHtml_WithAttachmentDispositionButContentId_RewritesCidReference()
    {
        // Arrange - same bug scenario but for HTML
        var html = "<img height=\"324\" src=\"cid:421002300@31012006-05B3\" width=\"518\">";
        var attachments = new List<Attachment>
        {
            CreateAttachment(
                messageId: "msg1",
                filename: "image001.jpg",
                filePath: "transformed/Inbox/2006/01/attachments/email_attachments/image001.jpg",
                contentId: "421002300@31012006-05B3",
                isInline: false) // Content-Disposition: attachment
        };

        // Act
        var result = CidRewriteHelper.RewriteCidReferencesHtml(
            html,
            "transformed/Inbox/2006/01/email.html",
            attachments);

        // Assert
        result.Should().Contain("src=\"attachments/email_attachments/image001.jpg\"");
        result.Should().NotContain("cid:");
    }

    [Fact]
    public void RewriteCidReferencesHtml_WithSingleQuotes_RewritesCidReference()
    {
        // Arrange
        var html = "<img src='cid:image001@test.local'>";
        var attachments = new List<Attachment>
        {
            CreateAttachment(
                messageId: "msg1",
                filename: "image001.jpg",
                filePath: "transformed/Inbox/2024/01/images/email_1.jpg",
                contentId: "image001@test.local",
                isInline: true)
        };

        // Act
        var result = CidRewriteHelper.RewriteCidReferencesHtml(
            html,
            "transformed/Inbox/2024/01/email.html",
            attachments);

        // Assert
        result.Should().Contain("src='images/email_1.jpg'");
    }

    #endregion

    #region LookupCid Tests

    [Fact]
    public void LookupCid_WithExactMatch_ReturnsPath()
    {
        // Arrange
        var cidToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image001@test.local"] = "images/email_1.jpg"
        };

        // Act
        var result = CidRewriteHelper.LookupCid("image001@test.local", cidToPath);

        // Assert
        result.Should().Be("images/email_1.jpg");
    }

    [Fact]
    public void LookupCid_WithAngleBrackets_ReturnsPath()
    {
        // Arrange - CID value has angle brackets, dictionary key doesn't
        var cidToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image001@test.local"] = "images/email_1.jpg"
        };

        // Act
        var result = CidRewriteHelper.LookupCid("<image001@test.local>", cidToPath);

        // Assert
        result.Should().Be("images/email_1.jpg");
    }

    [Fact]
    public void LookupCid_CaseInsensitive_ReturnsPath()
    {
        // Arrange
        var cidToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["IMAGE001@TEST.LOCAL"] = "images/email_1.jpg"
        };

        // Act
        var result = CidRewriteHelper.LookupCid("image001@test.local", cidToPath);

        // Assert
        result.Should().Be("images/email_1.jpg");
    }

    [Fact]
    public void LookupCid_WithNoMatch_ReturnsNull()
    {
        // Arrange
        var cidToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image001@test.local"] = "images/email_1.jpg"
        };

        // Act
        var result = CidRewriteHelper.LookupCid("unknown@test.local", cidToPath);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region CalculateRelativePathToAttachment Tests

    [Fact]
    public void CalculateRelativePathToAttachment_SameDirectory_ReturnsFilename()
    {
        // Arrange
        var outputPath = "transformed/Inbox/2024/01/email.md";
        var attachmentPath = "transformed/Inbox/2024/01/image.jpg";

        // Act
        var result = CidRewriteHelper.CalculateRelativePathToAttachment(outputPath, attachmentPath);

        // Assert
        result.Should().Be("image.jpg");
    }

    [Fact]
    public void CalculateRelativePathToAttachment_InSubdirectory_ReturnsRelativePath()
    {
        // Arrange
        var outputPath = "transformed/Inbox/2024/01/email.md";
        var attachmentPath = "transformed/Inbox/2024/01/images/email_1.jpg";

        // Act
        var result = CidRewriteHelper.CalculateRelativePathToAttachment(outputPath, attachmentPath);

        // Assert
        result.Should().Be("images/email_1.jpg");
    }

    [Fact]
    public void CalculateRelativePathToAttachment_InAttachmentsSubdirectory_ReturnsRelativePath()
    {
        // Arrange - the exact structure from the bug report
        var outputPath = "transformed/Inbox/Daily Reports/DailyReports.Iris/2006/01/RE_ Iris Daily Progress Report - 30th January 2005_0023.md";
        var attachmentPath = "transformed/Inbox/Daily Reports/DailyReports.Iris/2006/01/attachments/RE_ Iris Daily Progress Report - 30th January 2005_0023_attachments/image001.jpg";

        // Act
        var result = CidRewriteHelper.CalculateRelativePathToAttachment(outputPath, attachmentPath);

        // Assert
        result.Should().Be("attachments/RE_ Iris Daily Progress Report - 30th January 2005_0023_attachments/image001.jpg");
    }

    [Fact]
    public void CalculateRelativePathToAttachment_DifferentBranch_ReturnsUpAndDownPath()
    {
        // Arrange
        var outputPath = "transformed/Inbox/2024/01/email.md";
        var attachmentPath = "transformed/Inbox/2024/02/other.jpg";

        // Act
        var result = CidRewriteHelper.CalculateRelativePathToAttachment(outputPath, attachmentPath);

        // Assert
        result.Should().Be("../02/other.jpg");
    }

    #endregion
}
