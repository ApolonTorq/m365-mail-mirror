using M365MailMirror.Infrastructure.Authentication;

namespace M365MailMirror.UnitTests.Authentication;

public class FileTokenCacheStorageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cacheFilePath;

    public FileTokenCacheStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"m365_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _cacheFilePath = Path.Combine(_tempDir, "test_token_cache.dat");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ReadAsync_WhenFileDoesNotExist_ReturnsNull()
    {
        var storage = new FileTokenCacheStorage(_cacheFilePath);

        var result = await storage.ReadAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_ThenReadAsync_ReturnsOriginalData()
    {
        var storage = new FileTokenCacheStorage(_cacheFilePath);
        var originalData = "test token data"u8.ToArray();

        await storage.WriteAsync(originalData);
        var result = await storage.ReadAsync();

        result.Should().BeEquivalentTo(originalData);
    }

    [Fact]
    public async Task ExistsAsync_WhenFileDoesNotExist_ReturnsFalse()
    {
        var storage = new FileTokenCacheStorage(_cacheFilePath);

        var exists = await storage.ExistsAsync();

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_AfterWriting_ReturnsTrue()
    {
        var storage = new FileTokenCacheStorage(_cacheFilePath);
        await storage.WriteAsync([1, 2, 3]);

        var exists = await storage.ExistsAsync();

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ClearAsync_RemovesFile()
    {
        var storage = new FileTokenCacheStorage(_cacheFilePath);
        await storage.WriteAsync([1, 2, 3]);

        await storage.ClearAsync();

        File.Exists(_cacheFilePath).Should().BeFalse();
    }

    [Fact]
    public async Task ClearAsync_WhenFileDoesNotExist_DoesNotThrow()
    {
        var storage = new FileTokenCacheStorage(_cacheFilePath);

        var action = async () => await storage.ClearAsync();

        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WriteAsync_CreatesDirectoryIfNotExists()
    {
        var nestedPath = Path.Combine(_tempDir, "nested", "dir", "cache.dat");
        var storage = new FileTokenCacheStorage(nestedPath);

        await storage.WriteAsync([1, 2, 3]);

        File.Exists(nestedPath).Should().BeTrue();
    }

    [Fact]
    public void StorageDescription_ContainsFilePath()
    {
        var storage = new FileTokenCacheStorage(_cacheFilePath);

        storage.StorageDescription.Should().Contain(_cacheFilePath);
    }

    [Fact]
    public async Task WriteAsync_ThenReadAsync_WithLargeData_WorksCorrectly()
    {
        var storage = new FileTokenCacheStorage(_cacheFilePath);
        var largeData = new byte[10000];
        Random.Shared.NextBytes(largeData);

        await storage.WriteAsync(largeData);
        var result = await storage.ReadAsync();

        result.Should().BeEquivalentTo(largeData);
    }

    [Fact]
    public async Task WriteAsync_OverwritesExistingFile()
    {
        var storage = new FileTokenCacheStorage(_cacheFilePath);
        var data1 = "first data"u8.ToArray();
        var data2 = "second data"u8.ToArray();

        await storage.WriteAsync(data1);
        await storage.WriteAsync(data2);
        var result = await storage.ReadAsync();

        result.Should().BeEquivalentTo(data2);
    }

    [Fact]
    public async Task ReadAsync_WithCorruptedFile_ReturnsNull()
    {
        var storage = new FileTokenCacheStorage(_cacheFilePath);

        // Write garbage data that will fail decryption
        File.WriteAllBytes(_cacheFilePath, [1, 2, 3, 4, 5]);

        var result = await storage.ReadAsync();

        result.Should().BeNull();
    }
}
