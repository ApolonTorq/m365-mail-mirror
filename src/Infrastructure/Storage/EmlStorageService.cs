using M365MailMirror.Core.Logging;
using M365MailMirror.Core.Storage;

namespace M365MailMirror.Infrastructure.Storage;

/// <summary>
/// Implementation of EML file storage with folder/date hierarchy.
/// Directory structure: eml/{folder}/{YYYY}/{MM}/{filename}.eml
/// </summary>
public class EmlStorageService : IEmlStorageService
{
    private readonly string _archiveRoot;
    private readonly IAppLogger _logger;

    /// <summary>
    /// The subdirectory name for EML files within the archive.
    /// </summary>
    public const string EmlDirectory = "eml";

    /// <summary>
    /// Maximum collision attempts before throwing an exception.
    /// </summary>
    private const int MaxCollisionAttempts = 1000;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmlStorageService"/> class.
    /// </summary>
    /// <param name="archiveRoot">The root directory for the mail archive.</param>
    /// <param name="logger">Optional logger instance.</param>
    public EmlStorageService(string archiveRoot, IAppLogger? logger = null)
    {
        _archiveRoot = Path.GetFullPath(archiveRoot);
        _logger = logger ?? LoggerFactory.CreateLogger<EmlStorageService>();
    }

    /// <inheritdoc />
    public async Task<string> StoreEmlAsync(
        Stream emlContent,
        string folderPath,
        string? subject,
        DateTimeOffset receivedTime,
        CancellationToken cancellationToken = default)
    {
        // Build the directory path: eml/{folder}/{YYYY}/{MM}/
        var sanitizedFolderPath = FilenameSanitizer.SanitizeFolderPath(folderPath);
        var dateSubPath = $"{receivedTime.Year:D4}{Path.DirectorySeparatorChar}{receivedTime.Month:D2}";
        var directoryPath = Path.Combine(EmlDirectory, sanitizedFolderPath, dateSubPath);
        var fullDirectoryPath = Path.Combine(_archiveRoot, directoryPath);

        // Ensure directory exists
        Directory.CreateDirectory(fullDirectoryPath);

        // Calculate max subject length for this path context
        var maxSubjectLength = FilenameSanitizer.CalculateMaxSubjectLength(
            _archiveRoot,
            sanitizedFolderPath,
            dateSubPath);

        // Generate filename and handle collisions
        var filename = FilenameSanitizer.GenerateEmlFilename(subject, receivedTime, maxSubjectLength);
        var fullPath = Path.Combine(fullDirectoryPath, filename);
        var relativePath = Path.Combine(directoryPath, filename);

        // Handle filename collisions
        var collisionCounter = 1;
        while (File.Exists(fullPath))
        {
            if (collisionCounter >= MaxCollisionAttempts)
            {
                throw new InvalidOperationException(
                    $"Unable to find unique filename after {MaxCollisionAttempts} attempts: {filename}");
            }

            filename = FilenameSanitizer.GenerateEmlFilenameWithCounter(
                subject,
                receivedTime,
                collisionCounter,
                maxSubjectLength);

            fullPath = Path.Combine(fullDirectoryPath, filename);
            relativePath = Path.Combine(directoryPath, filename);
            collisionCounter++;
        }

        // Write atomically using temp file then rename
        // Use GUID in temp filename to prevent race conditions in parallel downloads
        var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var fileStream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await emlContent.CopyToAsync(fileStream, cancellationToken);
                await fileStream.FlushAsync(cancellationToken);
            }

            // Atomic rename (on most filesystems)
            File.Move(tempPath, fullPath, overwrite: false);

            _logger.Debug("Stored EML file: {0}", relativePath);
            return relativePath;
        }
        catch (Exception ex)
        {
            // Clean up temp file on failure
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }

            _logger.Error(ex, "Failed to store EML file: {0}", relativePath);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<string> MoveEmlAsync(
        string sourcePath,
        string destinationFolderPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sourceFullPath = GetFullPath(sourcePath);
        if (!File.Exists(sourceFullPath))
        {
            throw new FileNotFoundException($"Source EML file not found: {sourcePath}", sourcePath);
        }

        // Extract filename from source path
        var filename = Path.GetFileName(sourcePath);

        // Build destination path preserving date structure
        // Extract date parts from source path if possible
        var sourceDir = Path.GetDirectoryName(sourcePath);
        string dateSubPath;

        if (sourceDir != null)
        {
            // Try to extract YYYY/MM from the path
            var parts = sourceDir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Length >= 2 &&
                int.TryParse(parts[^2], out var year) &&
                int.TryParse(parts[^1], out var month))
            {
                dateSubPath = $"{year:D4}{Path.DirectorySeparatorChar}{month:D2}";
            }
            else
            {
                // Fallback to current date
                var now = DateTimeOffset.UtcNow;
                dateSubPath = $"{now.Year:D4}{Path.DirectorySeparatorChar}{now.Month:D2}";
            }
        }
        else
        {
            var now = DateTimeOffset.UtcNow;
            dateSubPath = $"{now.Year:D4}{Path.DirectorySeparatorChar}{now.Month:D2}";
        }

        var sanitizedDestFolder = FilenameSanitizer.SanitizeFolderPath(destinationFolderPath);
        var destDirectoryPath = Path.Combine(EmlDirectory, sanitizedDestFolder, dateSubPath);
        var destFullDirectory = Path.Combine(_archiveRoot, destDirectoryPath);

        // Ensure destination directory exists
        Directory.CreateDirectory(destFullDirectory);

        var destFullPath = Path.Combine(destFullDirectory, filename);
        var destRelativePath = Path.Combine(destDirectoryPath, filename);

        // Handle collision at destination
        var collisionCounter = 1;
        var baseFilename = Path.GetFileNameWithoutExtension(filename);
        var extension = Path.GetExtension(filename);

        while (File.Exists(destFullPath))
        {
            if (collisionCounter >= MaxCollisionAttempts)
            {
                throw new InvalidOperationException(
                    $"Unable to find unique filename at destination after {MaxCollisionAttempts} attempts: {filename}");
            }

            filename = $"{baseFilename}_{collisionCounter}{extension}";
            destFullPath = Path.Combine(destFullDirectory, filename);
            destRelativePath = Path.Combine(destDirectoryPath, filename);
            collisionCounter++;
        }

        File.Move(sourceFullPath, destFullPath, overwrite: false);

        _logger.Debug("Moved EML file from {0} to {1}", sourcePath, destRelativePath);
        return Task.FromResult(destRelativePath);
    }

    /// <inheritdoc />
    public bool Exists(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);
        return File.Exists(fullPath);
    }

    /// <inheritdoc />
    public void Delete(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.Debug("Deleted EML file: {0}", relativePath);
        }
    }

    /// <inheritdoc />
    public string GetFullPath(string relativePath)
    {
        // Ensure path is safe (no traversal attacks)
        var fullPath = Path.GetFullPath(Path.Combine(_archiveRoot, relativePath));

        if (!fullPath.StartsWith(_archiveRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Path traversal detected: {relativePath}", nameof(relativePath));
        }

        return fullPath;
    }

    /// <inheritdoc />
    public Stream OpenRead(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);
        return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    /// <inheritdoc />
    public long GetFileSize(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);
        var fileInfo = new FileInfo(fullPath);
        return fileInfo.Length;
    }

    /// <summary>
    /// The subdirectory name for quarantined files.
    /// </summary>
    public const string QuarantineDirectory = "_Quarantine";

    /// <inheritdoc />
    public Task<string> MoveToQuarantineAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sourceFullPath = GetFullPath(relativePath);
        if (!File.Exists(sourceFullPath))
        {
            throw new FileNotFoundException($"Source EML file not found: {relativePath}", relativePath);
        }

        // Build quarantine path: _Quarantine/{original-relative-path}
        var quarantinePath = Path.Combine(QuarantineDirectory, relativePath);
        var destFullPath = Path.Combine(_archiveRoot, quarantinePath);
        var destDirectory = Path.GetDirectoryName(destFullPath);

        // Ensure destination directory exists
        if (!string.IsNullOrEmpty(destDirectory))
        {
            Directory.CreateDirectory(destDirectory);
        }

        // Handle collision at destination (shouldn't happen but be safe)
        var filename = Path.GetFileName(relativePath);
        var collisionCounter = 1;
        var baseFilename = Path.GetFileNameWithoutExtension(filename);
        var extension = Path.GetExtension(filename);

        while (File.Exists(destFullPath))
        {
            if (collisionCounter >= MaxCollisionAttempts)
            {
                throw new InvalidOperationException(
                    $"Unable to find unique filename in quarantine after {MaxCollisionAttempts} attempts: {filename}");
            }

            var newFilename = $"{baseFilename}_{collisionCounter}{extension}";
            quarantinePath = Path.Combine(
                QuarantineDirectory,
                Path.GetDirectoryName(relativePath) ?? string.Empty,
                newFilename);
            destFullPath = Path.Combine(_archiveRoot, quarantinePath);
            collisionCounter++;
        }

        File.Move(sourceFullPath, destFullPath, overwrite: false);

        _logger.Debug("Quarantined EML file from {0} to {1}", relativePath, quarantinePath);
        return Task.FromResult(quarantinePath);
    }

    /// <inheritdoc />
    public void CleanupOrphanedTempFiles(TimeSpan maxAge)
    {
        var emlRoot = Path.Combine(_archiveRoot, EmlDirectory);
        if (!Directory.Exists(emlRoot))
        {
            return;
        }

        var cutoff = DateTime.UtcNow - maxAge;
        var cleanedCount = 0;

        foreach (var tmpFile in Directory.EnumerateFiles(emlRoot, "*.tmp", SearchOption.AllDirectories))
        {
            try
            {
                var fileInfo = new FileInfo(tmpFile);
                if (fileInfo.LastWriteTimeUtc < cutoff)
                {
                    File.Delete(tmpFile);
                    cleanedCount++;
                    _logger.Debug("Cleaned up orphaned temp file: {0}", tmpFile);
                }
            }
            catch
            {
                // Ignore cleanup errors - file may be in use or already deleted
            }
        }

        if (cleanedCount > 0)
        {
            _logger.Info("Cleaned up {0} orphaned temp files", cleanedCount);
        }
    }
}
