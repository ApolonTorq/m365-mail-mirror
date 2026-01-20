using System.IO.Compression;
using System.Text;
using M365MailMirror.Core.Configuration;
using M365MailMirror.Core.Security;

namespace M365MailMirror.UnitTests.Security;

/// <summary>
/// Unit tests for the ZipExtractor class.
/// </summary>
public class ZipExtractorTests : IDisposable
{
    private readonly string _testDir;
    private readonly ZipExtractor _extractor;

    public ZipExtractorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "ZipExtractorTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDir);
        _extractor = new ZipExtractor();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }

        GC.SuppressFinalize(this);
    }

    #region IsZipFile Tests

    [Theory]
    [InlineData("file.zip", true)]
    [InlineData("file.ZIP", true)]
    [InlineData("file.Zip", true)]
    [InlineData("file.txt", false)]
    [InlineData("file.exe", false)]
    [InlineData("file.zip.txt", false)]
    [InlineData("archive", false)]
    public void IsZipFile_ReturnsExpectedResult(string filename, bool expected)
    {
        var result = ZipExtractor.IsZipFile(filename);
        result.Should().Be(expected);
    }

    #endregion

    #region AnalyzeZip Tests

    [Fact]
    public void AnalyzeZip_ValidZipWithNormalFiles_CanExtract()
    {
        var zipPath = CreateTestZip("normal.zip", new[]
        {
            ("file1.txt", "Content 1"),
            ("folder/file2.txt", "Content 2"),
            ("folder/subfolder/file3.csv", "Col1,Col2\n1,2")
        });

        var result = _extractor.AnalyzeZip(zipPath);

        result.CanExtract.Should().BeTrue();
        result.SkipReason.Should().BeNull();
        result.FileCount.Should().Be(3);
        result.HasExecutables.Should().BeFalse();
        result.HasUnsafePaths.Should().BeFalse();
        result.IsEncrypted.Should().BeFalse();
    }

    [Fact]
    public void AnalyzeZip_ZipWithExecutable_ReturnsHasExecutables()
    {
        var zipPath = CreateTestZip("withexe.zip", new[]
        {
            ("readme.txt", "Read me"),
            ("program.exe", "fake exe content")
        });

        var config = new ZipExtractionConfiguration { SkipWithExecutables = true };
        var extractor = new ZipExtractor(config);

        var result = extractor.AnalyzeZip(zipPath);

        result.CanExtract.Should().BeFalse();
        result.SkipReason.Should().Contain("executable");
        result.HasExecutables.Should().BeTrue();
        result.ExecutableEntries.Should().Contain("program.exe");
    }

    [Fact]
    public void AnalyzeZip_ZipWithExecutable_ExtractsIfAllowed()
    {
        var zipPath = CreateTestZip("withexe.zip", new[]
        {
            ("readme.txt", "Read me"),
            ("program.exe", "fake exe content")
        });

        var config = new ZipExtractionConfiguration { SkipWithExecutables = false };
        var extractor = new ZipExtractor(config);

        var result = extractor.AnalyzeZip(zipPath);

        result.CanExtract.Should().BeTrue();
        result.HasExecutables.Should().BeTrue();
    }

    [Fact]
    public void AnalyzeZip_ZipWithPathTraversal_ReturnsHasUnsafePaths()
    {
        // Create a ZIP with path traversal - need to use ZipArchive directly
        var zipPath = Path.Combine(_testDir, "traversal.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("../../../etc/passwd");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("root:x:0:0");
        }

        var result = _extractor.AnalyzeZip(zipPath);

        result.CanExtract.Should().BeFalse();
        result.SkipReason.Should().Contain("unsafe paths");
        result.HasUnsafePaths.Should().BeTrue();
    }

    [Fact]
    public void AnalyzeZip_ZipWithAbsolutePath_ReturnsHasUnsafePaths()
    {
        var zipPath = Path.Combine(_testDir, "absolute.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("/etc/passwd");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("content");
        }

        var result = _extractor.AnalyzeZip(zipPath);

        result.CanExtract.Should().BeFalse();
        result.HasUnsafePaths.Should().BeTrue();
    }

    [Fact]
    public void AnalyzeZip_TooManyFiles_SkipsExtraction()
    {
        var files = Enumerable.Range(1, 150)
            .Select(i => ($"file{i}.txt", $"Content {i}"))
            .ToArray();

        var zipPath = CreateTestZip("manyfiles.zip", files);

        var config = new ZipExtractionConfiguration { MaxFiles = 100 };
        var extractor = new ZipExtractor(config);

        var result = extractor.AnalyzeZip(zipPath);

        result.CanExtract.Should().BeFalse();
        result.SkipReason.Should().Contain("Too many files");
        result.FileCount.Should().Be(150);
    }

    [Fact]
    public void AnalyzeZip_TooFewFiles_SkipsExtraction()
    {
        var zipPath = Path.Combine(_testDir, "empty.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Create an empty ZIP (just directories)
            zip.CreateEntry("emptydir/");
        }

        var config = new ZipExtractionConfiguration { MinFiles = 1 };
        var extractor = new ZipExtractor(config);

        var result = extractor.AnalyzeZip(zipPath);

        result.CanExtract.Should().BeFalse();
        result.SkipReason.Should().Contain("Too few files");
        result.FileCount.Should().Be(0);
    }

    [Fact]
    public void AnalyzeZip_NonExistentFile_ReturnsCannotExtract()
    {
        var result = _extractor.AnalyzeZip("/nonexistent/file.zip");

        result.CanExtract.Should().BeFalse();
        result.SkipReason.Should().Contain("does not exist");
    }

    [Fact]
    public void AnalyzeZip_InvalidZip_ReturnsCannotExtract()
    {
        var invalidPath = Path.Combine(_testDir, "invalid.zip");
        File.WriteAllText(invalidPath, "This is not a ZIP file");

        var result = _extractor.AnalyzeZip(invalidPath);

        result.CanExtract.Should().BeFalse();
        result.SkipReason.Should().NotBeNull();
    }

    #endregion

    #region ExtractAsync Tests

    [Fact]
    public async Task ExtractAsync_ValidZip_ExtractsAllFiles()
    {
        var zipPath = CreateTestZip("extract.zip", new[]
        {
            ("file1.txt", "Content 1"),
            ("folder/file2.txt", "Content 2")
        });

        var extractDir = Path.Combine(_testDir, "extracted");

        var result = await _extractor.ExtractAsync(zipPath, extractDir);

        result.Extracted.Should().BeTrue();
        result.FileCount.Should().Be(2);
        result.ExtractedFiles.Should().HaveCount(2);
        File.Exists(Path.Combine(extractDir, "file1.txt")).Should().BeTrue();
        File.Exists(Path.Combine(extractDir, "folder", "file2.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_DisabledExtraction_Skips()
    {
        var zipPath = CreateTestZip("disabled.zip", new[] { ("file.txt", "content") });
        var extractDir = Path.Combine(_testDir, "extracted_disabled");

        var config = new ZipExtractionConfiguration { Enabled = false };
        var extractor = new ZipExtractor(config);

        var result = await extractor.ExtractAsync(zipPath, extractDir);

        result.Extracted.Should().BeFalse();
        result.SkipReason.Should().Contain("disabled");
    }

    [Fact]
    public async Task ExtractAsync_WithUnsafeEntry_SkipsUnsafeEntries()
    {
        // This tests that even if analysis passes, we double-check during extraction
        var zipPath = CreateTestZip("safe.zip", new[]
        {
            ("safe_file.txt", "Safe content"),
            ("another_safe.txt", "Also safe")
        });

        var extractDir = Path.Combine(_testDir, "extracted_safe");

        var result = await _extractor.ExtractAsync(zipPath, extractDir);

        result.Extracted.Should().BeTrue();
        result.FileCount.Should().Be(2);
    }

    [Fact]
    public async Task ExtractAsync_FileCollision_HandlesIt()
    {
        var extractDir = Path.Combine(_testDir, "extracted_collision");
        Directory.CreateDirectory(extractDir);

        // Create a file that will collide
        File.WriteAllText(Path.Combine(extractDir, "file.txt"), "Existing content");

        var zipPath = CreateTestZip("collision.zip", new[]
        {
            ("file.txt", "New content")
        });

        var result = await _extractor.ExtractAsync(zipPath, extractDir);

        result.Extracted.Should().BeTrue();
        result.FileCount.Should().Be(1);

        // Should have both files now
        File.Exists(Path.Combine(extractDir, "file.txt")).Should().BeTrue();
        File.Exists(Path.Combine(extractDir, "file_1.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_PreservesDirectoryStructure()
    {
        var zipPath = CreateTestZip("structure.zip", new[]
        {
            ("root.txt", "Root"),
            ("a/file_a.txt", "A"),
            ("a/b/file_ab.txt", "AB"),
            ("a/b/c/file_abc.txt", "ABC")
        });

        var extractDir = Path.Combine(_testDir, "extracted_structure");

        var result = await _extractor.ExtractAsync(zipPath, extractDir);

        result.Extracted.Should().BeTrue();
        result.FileCount.Should().Be(4);

        Directory.Exists(Path.Combine(extractDir, "a")).Should().BeTrue();
        Directory.Exists(Path.Combine(extractDir, "a", "b")).Should().BeTrue();
        Directory.Exists(Path.Combine(extractDir, "a", "b", "c")).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var zipPath = CreateTestZip("cancel.zip", new[] { ("file.txt", "content") });
        var extractDir = Path.Combine(_testDir, "extracted_cancel");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _extractor.ExtractAsync(zipPath, extractDir, cts.Token));
    }

    #endregion

    #region Helper Methods

    private string CreateTestZip(string filename, (string path, string content)[] files)
    {
        var zipPath = Path.Combine(_testDir, filename);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        foreach (var (path, content) in files)
        {
            var entry = zip.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return zipPath;
    }

    #endregion
}
