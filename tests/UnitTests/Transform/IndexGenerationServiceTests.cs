using M365MailMirror.Core.Database;
using M365MailMirror.Core.Database.Entities;
using M365MailMirror.Core.Transform;
using M365MailMirror.Infrastructure.Transform;
using Moq;

namespace M365MailMirror.UnitTests.Transform;

public class IndexGenerationServiceTests : IDisposable
{
    private readonly Mock<IStateDatabase> _mockDatabase;
    private readonly string _testArchiveRoot;
    private readonly IndexGenerationService _service;

    public IndexGenerationServiceTests()
    {
        _mockDatabase = new Mock<IStateDatabase>();
        _testArchiveRoot = Path.Combine(Path.GetTempPath(), $"IndexGenTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testArchiveRoot);
        _service = new IndexGenerationService(_mockDatabase.Object, _testArchiveRoot, logger: null);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testArchiveRoot))
        {
            Directory.Delete(_testArchiveRoot, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullDatabase_ThrowsArgumentNullException()
    {
        var act = () => new IndexGenerationService(null!, _testArchiveRoot, logger: null);

        act.Should().Throw<ArgumentNullException>().WithParameterName("database");
    }

    [Fact]
    public void Constructor_NullArchiveRoot_ThrowsArgumentNullException()
    {
        var act = () => new IndexGenerationService(_mockDatabase.Object, null!, logger: null);

        act.Should().Throw<ArgumentNullException>().WithParameterName("archiveRoot");
    }

    #endregion

    #region GenerateIndexesAsync Tests - No Messages

    [Fact]
    public async Task GenerateIndexesAsync_NoMessages_ReturnsSuccessWithZeroCounts()
    {
        _mockDatabase.Setup(x => x.GetDistinctYearMonthsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(int Year, int Month)>());

        var result = await _service.GenerateIndexesAsync(
            new IndexGenerationOptions { GenerateHtmlIndexes = true, GenerateMarkdownIndexes = true },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.HtmlIndexesGenerated.Should().Be(0);
        result.MarkdownIndexesGenerated.Should().Be(0);
        result.Errors.Should().Be(0);
    }

    #endregion

    #region GenerateIndexesAsync Tests - Single Month

    [Fact]
    public async Task GenerateIndexesAsync_SingleMonth_GeneratesHtmlIndexes()
    {
        SetupYearMonthWithMessages(2024, 1, 3);

        var result = await _service.GenerateIndexesAsync(
            new IndexGenerationOptions { GenerateHtmlIndexes = true, GenerateMarkdownIndexes = false },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.HtmlIndexesGenerated.Should().BeGreaterThan(0);
        result.MarkdownIndexesGenerated.Should().Be(0);

        // Verify root index exists
        var rootIndex = Path.Combine(_testArchiveRoot, "transformed", "index.html");
        File.Exists(rootIndex).Should().BeTrue();

        // Verify year index exists
        var yearIndex = Path.Combine(_testArchiveRoot, "transformed", "2024", "index.html");
        File.Exists(yearIndex).Should().BeTrue();

        // Verify month index exists
        var monthIndex = Path.Combine(_testArchiveRoot, "transformed", "2024", "01", "index.html");
        File.Exists(monthIndex).Should().BeTrue();
    }

    [Fact]
    public async Task GenerateIndexesAsync_SingleMonth_GeneratesMarkdownIndexes()
    {
        SetupYearMonthWithMessages(2024, 1, 3);

        var result = await _service.GenerateIndexesAsync(
            new IndexGenerationOptions { GenerateHtmlIndexes = false, GenerateMarkdownIndexes = true },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.HtmlIndexesGenerated.Should().Be(0);
        result.MarkdownIndexesGenerated.Should().BeGreaterThan(0);

        // Verify root index exists
        var rootIndex = Path.Combine(_testArchiveRoot, "transformed", "index.md");
        File.Exists(rootIndex).Should().BeTrue();

        // Verify month index exists
        var monthIndex = Path.Combine(_testArchiveRoot, "transformed", "2024", "01", "index.md");
        File.Exists(monthIndex).Should().BeTrue();
    }

    [Fact]
    public async Task GenerateIndexesAsync_SingleMonth_GeneratesBothFormats()
    {
        SetupYearMonthWithMessages(2024, 1, 3);

        var result = await _service.GenerateIndexesAsync(
            new IndexGenerationOptions { GenerateHtmlIndexes = true, GenerateMarkdownIndexes = true },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.HtmlIndexesGenerated.Should().BeGreaterThan(0);
        result.MarkdownIndexesGenerated.Should().BeGreaterThan(0);

        // Both formats should have the same count
        result.HtmlIndexesGenerated.Should().Be(result.MarkdownIndexesGenerated);
    }

    #endregion

    #region Index Content Tests

    [Fact]
    public async Task GenerateIndexesAsync_RootIndex_ContainsLinkToYear()
    {
        SetupYearMonthWithMessages(2024, 1, 2);

        await _service.GenerateIndexesAsync(
            new IndexGenerationOptions { GenerateHtmlIndexes = true, GenerateMarkdownIndexes = false },
            CancellationToken.None);

        var rootIndex = Path.Combine(_testArchiveRoot, "transformed", "index.html");
        var content = await File.ReadAllTextAsync(rootIndex);

        content.Should().Contain("2024/index.html");
        content.Should().Contain("Mail Archive");
        content.Should().Contain("Archive");
    }

    [Fact]
    public async Task GenerateIndexesAsync_MonthIndex_ContainsEmailLinks()
    {
        SetupYearMonthWithMessages(2024, 1, 2);

        await _service.GenerateIndexesAsync(
            new IndexGenerationOptions { GenerateHtmlIndexes = true, GenerateMarkdownIndexes = false },
            CancellationToken.None);

        var monthIndex = Path.Combine(_testArchiveRoot, "transformed", "2024", "01", "index.html");
        var content = await File.ReadAllTextAsync(monthIndex);

        content.Should().Contain("Test Subject 1");
        content.Should().Contain("Test Subject 2");
        content.Should().Contain(".html");
        content.Should().Contain("sender@example.com");
    }

    [Fact]
    public async Task GenerateIndexesAsync_HtmlIndex_ContainsBreadcrumbNavigation()
    {
        SetupYearMonthWithMessages(2024, 1, 1);

        await _service.GenerateIndexesAsync(
            new IndexGenerationOptions { GenerateHtmlIndexes = true, GenerateMarkdownIndexes = false },
            CancellationToken.None);

        var monthIndex = Path.Combine(_testArchiveRoot, "transformed", "2024", "01", "index.html");
        var content = await File.ReadAllTextAsync(monthIndex);

        content.Should().Contain("breadcrumb");
        content.Should().Contain("Archive");
        content.Should().Contain("2024");
    }

    [Fact]
    public async Task GenerateIndexesAsync_HtmlIndex_ContainsUpLink()
    {
        SetupYearMonthWithMessages(2024, 1, 1);

        await _service.GenerateIndexesAsync(
            new IndexGenerationOptions { GenerateHtmlIndexes = true, GenerateMarkdownIndexes = false },
            CancellationToken.None);

        var yearIndex = Path.Combine(_testArchiveRoot, "transformed", "2024", "index.html");
        var content = await File.ReadAllTextAsync(yearIndex);

        content.Should().Contain("../index.html");
        content.Should().Contain("Up");
    }

    [Fact]
    public async Task GenerateIndexesAsync_MarkdownIndex_UsesMarkdownFormat()
    {
        SetupYearMonthWithMessages(2024, 1, 2);

        await _service.GenerateIndexesAsync(
            new IndexGenerationOptions { GenerateHtmlIndexes = false, GenerateMarkdownIndexes = true },
            CancellationToken.None);

        var monthIndex = Path.Combine(_testArchiveRoot, "transformed", "2024", "01", "index.md");
        var content = await File.ReadAllTextAsync(monthIndex);

        // Should contain markdown table format
        content.Should().Contain("| Subject |");
        content.Should().Contain("|---------|");
        content.Should().Contain(".md)");
    }

    #endregion

    #region Multiple Years Tests

    [Fact]
    public async Task GenerateIndexesAsync_MultipleYears_GeneratesIndexesForAll()
    {
        SetupMultipleYearMonths([(2023, 6), (2023, 12), (2024, 1), (2024, 2)]);

        var result = await _service.GenerateIndexesAsync(
            new IndexGenerationOptions { GenerateHtmlIndexes = true, GenerateMarkdownIndexes = false },
            CancellationToken.None);

        result.Success.Should().BeTrue();

        // Root should link to both years
        var rootIndex = Path.Combine(_testArchiveRoot, "transformed", "index.html");
        var content = await File.ReadAllTextAsync(rootIndex);

        content.Should().Contain("2023");
        content.Should().Contain("2024");
    }

    #endregion

    #region Multiple Months Tests

    [Fact]
    public async Task GenerateIndexesAsync_MultipleMonths_GeneratesMonthIndexes()
    {
        SetupMultipleYearMonths([(2024, 1), (2024, 2), (2024, 3)]);

        var result = await _service.GenerateIndexesAsync(
            new IndexGenerationOptions { GenerateHtmlIndexes = true, GenerateMarkdownIndexes = false },
            CancellationToken.None);

        result.Success.Should().BeTrue();

        // Year index should link to all months
        var yearIndex = Path.Combine(_testArchiveRoot, "transformed", "2024", "index.html");
        var content = await File.ReadAllTextAsync(yearIndex);

        content.Should().Contain("January");
        content.Should().Contain("February");
        content.Should().Contain("March");
    }

    [Fact]
    public async Task GenerateIndexesAsync_MonthLinks_UseNumericFolderNames()
    {
        SetupMultipleYearMonths([(2024, 1), (2024, 2), (2024, 12)]);

        var result = await _service.GenerateIndexesAsync(
            new IndexGenerationOptions { GenerateHtmlIndexes = true, GenerateMarkdownIndexes = true },
            CancellationToken.None);

        result.Success.Should().BeTrue();

        // HTML: Year index should use numeric folder paths for month links
        var htmlYearIndex = Path.Combine(_testArchiveRoot, "transformed", "2024", "index.html");
        var htmlContent = await File.ReadAllTextAsync(htmlYearIndex);

        // Links should use "01/index.html", not "January/index.html"
        htmlContent.Should().Contain("01/index.html");
        htmlContent.Should().Contain("02/index.html");
        htmlContent.Should().Contain("12/index.html");
        htmlContent.Should().NotContain("January/index.html");
        htmlContent.Should().NotContain("February/index.html");
        htmlContent.Should().NotContain("December/index.html");

        // Markdown: Year index should use numeric folder paths for month links
        var mdYearIndex = Path.Combine(_testArchiveRoot, "transformed", "2024", "index.md");
        var mdContent = await File.ReadAllTextAsync(mdYearIndex);

        // Links should use "01/index.md", not "January/index.md"
        mdContent.Should().Contain("01/index.md");
        mdContent.Should().Contain("02/index.md");
        mdContent.Should().Contain("12/index.md");
        mdContent.Should().NotContain("January/index.md");
        mdContent.Should().NotContain("February/index.md");
        mdContent.Should().NotContain("December/index.md");
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task GenerateIndexesAsync_CancellationRequested_ReturnsCancelledResult()
    {
        SetupYearMonthWithMessages(2024, 1, 1);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _service.GenerateIndexesAsync(
            new IndexGenerationOptions { GenerateHtmlIndexes = true, GenerateMarkdownIndexes = true },
            cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cancelled");
    }

    #endregion

    #region Attachment Indicator Tests

    [Fact]
    public async Task GenerateIndexesAsync_MessageWithAttachment_ShowsAttachmentIcon()
    {
        SetupSingleMessageWithAttachment(2024, 1);

        await _service.GenerateIndexesAsync(
            new IndexGenerationOptions { GenerateHtmlIndexes = true, GenerateMarkdownIndexes = false },
            CancellationToken.None);

        var monthIndex = Path.Combine(_testArchiveRoot, "transformed", "2024", "01", "index.html");
        var content = await File.ReadAllTextAsync(monthIndex);

        // HTML entity for paperclip
        content.Should().Contain("&#128206;");
    }

    #endregion

    #region Message Count Tests

    [Fact]
    public async Task GenerateIndexesAsync_Index_ContainsMessageCount()
    {
        SetupYearMonthWithMessages(2024, 1, 5);

        await _service.GenerateIndexesAsync(
            new IndexGenerationOptions { GenerateHtmlIndexes = true, GenerateMarkdownIndexes = false },
            CancellationToken.None);

        var rootIndex = Path.Combine(_testArchiveRoot, "transformed", "index.html");
        var content = await File.ReadAllTextAsync(rootIndex);

        content.Should().Contain("5 messages");
    }

    [Fact]
    public async Task GenerateIndexesAsync_SingleMessage_UseSingularForm()
    {
        SetupYearMonthWithMessages(2024, 1, 1);

        await _service.GenerateIndexesAsync(
            new IndexGenerationOptions { GenerateHtmlIndexes = true, GenerateMarkdownIndexes = false },
            CancellationToken.None);

        var rootIndex = Path.Combine(_testArchiveRoot, "transformed", "index.html");
        var content = await File.ReadAllTextAsync(rootIndex);

        content.Should().Contain("1 message");
        content.Should().NotContain("1 messages");
    }

    #endregion

    #region Style Tests

    [Fact]
    public async Task GenerateIndexesAsync_HtmlIndex_ContainsOutlookLikeStyling()
    {
        SetupYearMonthWithMessages(2024, 1, 1);

        await _service.GenerateIndexesAsync(
            new IndexGenerationOptions { GenerateHtmlIndexes = true, GenerateMarkdownIndexes = false },
            CancellationToken.None);

        var rootIndex = Path.Combine(_testArchiveRoot, "transformed", "index.html");
        var content = await File.ReadAllTextAsync(rootIndex);

        // Outlook-like blue header
        content.Should().Contain("#0078d4");
        content.Should().Contain("Segoe UI");
    }

    #endregion

    #region Helper Methods

    private void SetupYearMonthWithMessages(int year, int month, int messageCount)
    {
        _mockDatabase.Setup(x => x.GetDistinctYearMonthsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(int Year, int Month)> { (year, month) });

        var messages = CreateTestMessages(year, month, messageCount);
        _mockDatabase.Setup(x => x.GetMessagesForIndexAsync(year, month, It.IsAny<CancellationToken>()))
            .ReturnsAsync(messages);
    }

    private void SetupMultipleYearMonths((int Year, int Month)[] yearMonths)
    {
        _mockDatabase.Setup(x => x.GetDistinctYearMonthsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(yearMonths.ToList());

        foreach (var (year, month) in yearMonths)
        {
            var messages = CreateTestMessages(year, month, 1);
            _mockDatabase.Setup(x => x.GetMessagesForIndexAsync(year, month, It.IsAny<CancellationToken>()))
                .ReturnsAsync(messages);
        }
    }

    private void SetupSingleMessageWithAttachment(int year, int month)
    {
        _mockDatabase.Setup(x => x.GetDistinctYearMonthsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(int Year, int Month)> { (year, month) });

        var messages = new List<Message>
        {
            new()
            {
                GraphId = "msg-1",
                ImmutableId = "imm-1",
                LocalPath = $"eml/{year}/{month:D2}/inbox_{year}-{month:D2}-15-10-00-00_email-with-attachment.eml",
                FolderPath = "Inbox",
                Subject = "Email with attachment",
                Sender = "sender@example.com",
                ReceivedTime = new DateTimeOffset(year, month, 15, 10, 0, 0, TimeSpan.Zero),
                Size = 1000,
                HasAttachments = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };

        _mockDatabase.Setup(x => x.GetMessagesForIndexAsync(year, month, It.IsAny<CancellationToken>()))
            .ReturnsAsync(messages);
    }

    private static List<Message> CreateTestMessages(int year, int month, int count)
    {
        var messages = new List<Message>();
        for (var i = 1; i <= count; i++)
        {
            messages.Add(new Message
            {
                GraphId = $"msg-{i}",
                ImmutableId = $"imm-{i}",
                LocalPath = $"eml/{year}/{month:D2}/inbox_{year}-{month:D2}-{i:D2}-10-00-00_test-subject-{i}.eml",
                FolderPath = "Inbox",
                Subject = $"Test Subject {i}",
                Sender = "sender@example.com",
                ReceivedTime = new DateTimeOffset(year, month, i, 10, 0, 0, TimeSpan.Zero),
                Size = 1000,
                HasAttachments = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        return messages;
    }

    #endregion
}
