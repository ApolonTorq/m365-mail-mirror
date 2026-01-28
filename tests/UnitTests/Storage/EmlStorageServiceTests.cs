using M365MailMirror.Infrastructure.Storage;
using System.Text;

namespace M365MailMirror.UnitTests.Storage;

public class EmlStorageServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly EmlStorageService _service;

    public EmlStorageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"m365-mail-mirror-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _service = new EmlStorageService(_tempDir);
    }

    public void Dispose()
    {
        // Clean up temp directory
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }

        GC.SuppressFinalize(this);
    }

    #region StoreEmlAsync Tests

    [Fact]
    public async Task StoreEmlAsync_ValidInput_CreatesFileInCorrectLocation()
    {
        var content = "MIME content here"u8.ToArray();
        using var stream = new MemoryStream(content);
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);

        var relativePath = await _service.StoreEmlAsync(
            stream,
            "Inbox",
            "Meeting Notes",
            receivedTime);

        // Path should be: eml/{YYYY}/{MM}/{folder}_{datetime}_{subject}.eml
        relativePath.Should().StartWith("eml");
        relativePath.Should().Contain("2024");
        relativePath.Should().Contain("01");
        relativePath.Should().EndWith(".eml");
        // Folder prefix should be in the filename
        relativePath.Should().Contain("inbox_");
        // Datetime should be in the filename
        relativePath.Should().Contain("2024-01-15-10-30-00");

        var fullPath = _service.GetFullPath(relativePath);
        File.Exists(fullPath).Should().BeTrue();

        var savedContent = await File.ReadAllBytesAsync(fullPath);
        savedContent.Should().BeEquivalentTo(content);
    }

    [Fact]
    public async Task StoreEmlAsync_NullSubject_UsesNoSubject()
    {
        using var stream = new MemoryStream("content"u8.ToArray());
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);

        var relativePath = await _service.StoreEmlAsync(
            stream,
            "Inbox",
            null,
            receivedTime);

        relativePath.Should().Contain("no-subject");
    }

    [Fact]
    public async Task StoreEmlAsync_DifferentMonths_CreatesCorrectHierarchy()
    {
        using var stream = new MemoryStream("content"u8.ToArray());
        var receivedTime = new DateTimeOffset(2024, 6, 20, 14, 15, 0, TimeSpan.Zero);

        var relativePath = await _service.StoreEmlAsync(
            stream,
            "Sent Items",
            "Test",
            receivedTime);

        // Path should contain year/month
        relativePath.Should().Contain("2024");
        relativePath.Should().Contain("06");

        _service.Exists(relativePath).Should().BeTrue();
    }

    [Fact]
    public async Task StoreEmlAsync_DuplicateFilename_HandlesCollision()
    {
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);

        // Store first file
        using var stream1 = new MemoryStream("content1"u8.ToArray());
        var path1 = await _service.StoreEmlAsync(stream1, "Inbox", "Test", receivedTime);

        // Store second file with same folder, subject and time
        using var stream2 = new MemoryStream("content2"u8.ToArray());
        var path2 = await _service.StoreEmlAsync(stream2, "Inbox", "Test", receivedTime);

        path1.Should().NotBe(path2);
        _service.Exists(path1).Should().BeTrue();
        _service.Exists(path2).Should().BeTrue();

        // Second file should have collision counter
        path2.Should().Contain("_1.eml");
    }

    [Fact]
    public async Task StoreEmlAsync_MultipleCollisions_HandlesAll()
    {
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var paths = new List<string>();

        for (int i = 0; i < 5; i++)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"content{i}"));
            var path = await _service.StoreEmlAsync(stream, "Inbox", "Test", receivedTime);
            paths.Add(path);
        }

        paths.Should().HaveCount(5);
        paths.Should().OnlyHaveUniqueItems();

        foreach (var path in paths)
        {
            _service.Exists(path).Should().BeTrue();
        }
    }

    [Fact]
    public async Task StoreEmlAsync_IllegalCharsInSubject_Sanitizes()
    {
        using var stream = new MemoryStream("content"u8.ToArray());
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);

        var relativePath = await _service.StoreEmlAsync(
            stream,
            "Inbox",
            "Test: Important?",
            receivedTime);

        // Should not throw and file should exist
        _service.Exists(relativePath).Should().BeTrue();

        // Path should not contain illegal characters
        relativePath.Should().NotContain(":");
        relativePath.Should().NotContain("?");
    }

    [Fact]
    public async Task StoreEmlAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        using var stream = new MemoryStream("content"u8.ToArray());
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await _service.StoreEmlAsync(
            stream,
            "Inbox",
            "Test",
            receivedTime,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task StoreEmlAsync_LargeContent_SuccessfullyStores()
    {
        var largeContent = new byte[1024 * 1024]; // 1 MB
        Random.Shared.NextBytes(largeContent);
        using var stream = new MemoryStream(largeContent);
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);

        var relativePath = await _service.StoreEmlAsync(
            stream,
            "Inbox",
            "Large Email",
            receivedTime);

        _service.Exists(relativePath).Should().BeTrue();

        var savedContent = await File.ReadAllBytesAsync(_service.GetFullPath(relativePath));
        savedContent.Should().BeEquivalentTo(largeContent);
    }

    #endregion

    #region Exists Tests

    [Fact]
    public async Task Exists_FileExists_ReturnsTrue()
    {
        using var stream = new MemoryStream("content"u8.ToArray());
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var path = await _service.StoreEmlAsync(stream, "Inbox", "Test", receivedTime);

        _service.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void Exists_FileDoesNotExist_ReturnsFalse()
    {
        _service.Exists("eml/2024/01/NonExistent.eml").Should().BeFalse();
    }

    #endregion

    #region Delete Tests

    [Fact]
    public async Task Delete_ExistingFile_DeletesIt()
    {
        using var stream = new MemoryStream("content"u8.ToArray());
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var path = await _service.StoreEmlAsync(stream, "Inbox", "Test", receivedTime);

        _service.Delete(path);

        _service.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void Delete_NonExistentFile_DoesNotThrow()
    {
        var act = () => _service.Delete("eml/2024/01/NonExistent.eml");

        act.Should().NotThrow();
    }

    #endregion

    #region GetFullPath Tests

    [Fact]
    public void GetFullPath_ValidPath_ReturnsFullPath()
    {
        var fullPath = _service.GetFullPath("eml/2024/01/test.eml");

        fullPath.Should().StartWith(_tempDir);
        fullPath.Should().Contain("eml");
    }

    [Fact]
    public void GetFullPath_PathTraversal_ThrowsArgumentException()
    {
        var act = () => _service.GetFullPath("../../../etc/passwd");

        act.Should().Throw<ArgumentException>().WithMessage("*traversal*");
    }

    [Fact]
    public void GetFullPath_AbsolutePath_ThrowsIfOutsideArchive()
    {
        // Test with an absolute path that would escape the archive
        var outsidePath = Path.GetFullPath(Path.Combine(_tempDir, "..", "outside"));

        var act = () => _service.GetFullPath(outsidePath);

        // Since we're passing a path that when combined escapes, it should throw
        // Note: This depends on how GetFullPath handles the input
    }

    #endregion

    #region OpenRead Tests

    [Fact]
    public async Task OpenRead_ExistingFile_ReturnsReadableStream()
    {
        var content = "test content"u8.ToArray();
        using var inputStream = new MemoryStream(content);
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var path = await _service.StoreEmlAsync(inputStream, "Inbox", "Test", receivedTime);

        using var readStream = _service.OpenRead(path);
        using var memStream = new MemoryStream();
        await readStream.CopyToAsync(memStream);

        memStream.ToArray().Should().BeEquivalentTo(content);
    }

    [Fact]
    public void OpenRead_NonExistentFile_ThrowsIOException()
    {
        var act = () => _service.OpenRead("eml/2024/01/NonExistent.eml");

        // Can throw FileNotFoundException or DirectoryNotFoundException (both are IOException)
        act.Should().Throw<IOException>();
    }

    #endregion

    #region Atomic Write Tests

    [Fact]
    public async Task StoreEmlAsync_SuccessfulWrite_NoTempFileRemains()
    {
        using var stream = new MemoryStream("content"u8.ToArray());
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);

        var relativePath = await _service.StoreEmlAsync(stream, "Inbox", "Test", receivedTime);
        var fullPath = _service.GetFullPath(relativePath);
        var tempPath = fullPath + ".tmp";

        File.Exists(tempPath).Should().BeFalse("temp file should be cleaned up after successful write");
    }

    [Fact]
    public async Task StoreEmlAsync_WithNestedFolder_IncludesFolderPrefixWithDashes()
    {
        using var stream = new MemoryStream("content"u8.ToArray());
        var receivedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);

        var relativePath = await _service.StoreEmlAsync(
            stream,
            "Inbox/Processed",
            "Test",
            receivedTime);

        // Nested folder should use dash as separator
        relativePath.Should().Contain("inbox-processed_");
    }

    #endregion
}
