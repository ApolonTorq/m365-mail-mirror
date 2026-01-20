using System.IO.Compression;
using M365MailMirror.Core.Configuration;
using M365MailMirror.Core.Logging;
using M365MailMirror.Core.Storage;

namespace M365MailMirror.Core.Security;

/// <summary>
/// Result of a ZIP extraction analysis.
/// </summary>
public class ZipAnalysisResult
{
    /// <summary>
    /// Whether the ZIP can be safely extracted.
    /// </summary>
    public bool CanExtract { get; init; }

    /// <summary>
    /// Reason for skipping extraction (null if can extract).
    /// </summary>
    public string? SkipReason { get; init; }

    /// <summary>
    /// Total number of files in the ZIP.
    /// </summary>
    public int FileCount { get; init; }

    /// <summary>
    /// Total uncompressed size of all files.
    /// </summary>
    public long TotalUncompressedSize { get; init; }

    /// <summary>
    /// Whether the ZIP contains executable files.
    /// </summary>
    public bool HasExecutables { get; init; }

    /// <summary>
    /// Whether the ZIP contains unsafe paths.
    /// </summary>
    public bool HasUnsafePaths { get; init; }

    /// <summary>
    /// Whether the ZIP is encrypted/password-protected.
    /// </summary>
    public bool IsEncrypted { get; init; }

    /// <summary>
    /// List of entry paths that are unsafe.
    /// </summary>
    public IReadOnlyList<string> UnsafeEntries { get; init; } = [];

    /// <summary>
    /// List of entry paths that are executables.
    /// </summary>
    public IReadOnlyList<string> ExecutableEntries { get; init; } = [];
}

/// <summary>
/// Result of a ZIP extraction operation.
/// </summary>
public class ZipExtractionResult
{
    /// <summary>
    /// Whether extraction was performed.
    /// </summary>
    public bool Extracted { get; init; }

    /// <summary>
    /// Path to the extraction folder (relative to archive root).
    /// </summary>
    public string? ExtractionPath { get; init; }

    /// <summary>
    /// Reason for skipping extraction (null if extracted).
    /// </summary>
    public string? SkipReason { get; init; }

    /// <summary>
    /// Number of files extracted.
    /// </summary>
    public int FileCount { get; init; }

    /// <summary>
    /// Total size of extracted files in bytes.
    /// </summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>
    /// Analysis results from the ZIP.
    /// </summary>
    public ZipAnalysisResult? Analysis { get; init; }

    /// <summary>
    /// List of extracted file paths (relative to extraction folder).
    /// </summary>
    public IReadOnlyList<ExtractedFileInfo> ExtractedFiles { get; init; } = [];
}

/// <summary>
/// Information about an extracted file.
/// </summary>
public class ExtractedFileInfo
{
    /// <summary>
    /// Path relative to the extraction folder.
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// Full path to the extracted file.
    /// </summary>
    public required string ExtractedPath { get; init; }

    /// <summary>
    /// Size of the extracted file in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }
}

/// <summary>
/// Safely extracts ZIP files with security checks.
/// </summary>
public class ZipExtractor
{
    private readonly ZipExtractionConfiguration _config;
    private readonly IAppLogger _logger;

    /// <summary>
    /// Creates a new ZipExtractor with the specified configuration.
    /// </summary>
    public ZipExtractor(ZipExtractionConfiguration? config = null, IAppLogger? logger = null)
    {
        _config = config ?? new ZipExtractionConfiguration();
        _logger = logger ?? LoggerFactory.CreateLogger<ZipExtractor>();
    }

    /// <summary>
    /// Checks if a file is a ZIP archive by extension.
    /// </summary>
    public static bool IsZipFile(string filename)
    {
        var extension = Path.GetExtension(filename);
        return string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Analyzes a ZIP file without extracting it.
    /// </summary>
    /// <param name="zipPath">Full path to the ZIP file.</param>
    /// <returns>Analysis result.</returns>
    public ZipAnalysisResult AnalyzeZip(string zipPath)
    {
        if (!File.Exists(zipPath))
        {
            return new ZipAnalysisResult
            {
                CanExtract = false,
                SkipReason = "File does not exist"
            };
        }

        try
        {
            using var zipStream = File.OpenRead(zipPath);
            return AnalyzeZipStream(zipStream, Path.GetFileName(zipPath));
        }
        catch (InvalidDataException)
        {
            return new ZipAnalysisResult
            {
                CanExtract = false,
                SkipReason = "Invalid or corrupted ZIP file"
            };
        }
        catch (Exception ex)
        {
            _logger.Warning("Error analyzing ZIP {0}: {1}", zipPath, ex.Message);
            return new ZipAnalysisResult
            {
                CanExtract = false,
                SkipReason = $"Error analyzing ZIP: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Analyzes a ZIP file from a stream.
    /// </summary>
    /// <param name="zipStream">Stream containing the ZIP data.</param>
    /// <param name="filename">Filename for logging purposes.</param>
    /// <returns>Analysis result.</returns>
    public ZipAnalysisResult AnalyzeZipStream(Stream zipStream, string filename)
    {
        var unsafeEntries = new List<string>();
        var executableEntries = new List<string>();
        var fileCount = 0;
        var totalSize = 0L;
        var isEncrypted = false;

        try
        {
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

            foreach (var entry in archive.Entries)
            {
                // Skip directories
                if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    continue;
                }

                fileCount++;
                totalSize += entry.Length;

                // Check for encrypted entries
                // Note: ZipArchive doesn't directly expose encryption, but we can check via External Attributes
                // or try to open the stream. For simplicity, we check ExternalAttributes for common encryption flags
                // In practice, encrypted ZIPs will fail to open entries
                if (IsEntryEncrypted(entry))
                {
                    isEncrypted = true;
                }

                // Check for unsafe paths
                if (!SecurityHelper.IsZipEntrySafe(entry.FullName))
                {
                    unsafeEntries.Add(entry.FullName);
                }

                // Check for executables
                if (SecurityHelper.IsExecutable(entry.FullName))
                {
                    executableEntries.Add(entry.FullName);
                }
            }
        }
        catch (InvalidDataException ex)
        {
            // This often indicates encryption or corruption
            if (ex.Message.Contains("CRC", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase))
            {
                isEncrypted = true;
            }
            else
            {
                return new ZipAnalysisResult
                {
                    CanExtract = false,
                    SkipReason = "Invalid or corrupted ZIP file"
                };
            }
        }

        // Apply decision logic
        var canExtract = true;
        string? skipReason = null;

        // Check encryption
        if (isEncrypted && _config.SkipEncrypted)
        {
            canExtract = false;
            skipReason = "Encrypted/password-protected ZIP";
        }
        // Check unsafe paths
        else if (unsafeEntries.Count > 0)
        {
            canExtract = false;
            skipReason = $"Contains unsafe paths ({string.Join(", ", unsafeEntries.Take(3))}{(unsafeEntries.Count > 3 ? "..." : "")})";
        }
        // Check executables
        else if (executableEntries.Count > 0 && _config.SkipWithExecutables)
        {
            canExtract = false;
            skipReason = $"Contains executable files ({string.Join(", ", executableEntries.Take(3))}{(executableEntries.Count > 3 ? "..." : "")})";
        }
        // Check file count range
        else if (fileCount < _config.MinFiles)
        {
            canExtract = false;
            skipReason = $"Too few files ({fileCount}, minimum: {_config.MinFiles})";
        }
        else if (fileCount > _config.MaxFiles)
        {
            canExtract = false;
            skipReason = $"Too many files ({fileCount}, maximum: {_config.MaxFiles})";
        }

        return new ZipAnalysisResult
        {
            CanExtract = canExtract,
            SkipReason = skipReason,
            FileCount = fileCount,
            TotalUncompressedSize = totalSize,
            HasExecutables = executableEntries.Count > 0,
            HasUnsafePaths = unsafeEntries.Count > 0,
            IsEncrypted = isEncrypted,
            UnsafeEntries = unsafeEntries,
            ExecutableEntries = executableEntries
        };
    }

    /// <summary>
    /// Extracts a ZIP file to the specified destination.
    /// </summary>
    /// <param name="zipPath">Full path to the ZIP file.</param>
    /// <param name="destinationPath">Full path to the extraction folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extraction result.</returns>
    public async Task<ZipExtractionResult> ExtractAsync(
        string zipPath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
        {
            return new ZipExtractionResult
            {
                Extracted = false,
                SkipReason = "ZIP extraction is disabled"
            };
        }

        // Analyze the ZIP first
        var analysis = AnalyzeZip(zipPath);

        if (!analysis.CanExtract)
        {
            // Log with appropriate detail based on skip reason
            var reason = analysis.SkipReason ?? "unknown";
            if (analysis.IsEncrypted)
            {
                _logger.Warning("Skipped encrypted ZIP: {0} - {1}", Path.GetFileName(zipPath), destinationPath);
            }
            else if (analysis.HasUnsafePaths)
            {
                var unsafeSample = analysis.UnsafeEntries.Count > 0 ? analysis.UnsafeEntries[0] : "unknown";
                _logger.Warning("Skipped ZIP with unsafe paths: {0} (contains {1})", Path.GetFileName(zipPath), unsafeSample);
            }
            else if (analysis.HasExecutables)
            {
                var exeSample = analysis.ExecutableEntries.Count > 0 ? analysis.ExecutableEntries[0] : "unknown";
                _logger.Warning("Skipped ZIP with executables: {0} (contains {1})", Path.GetFileName(zipPath), exeSample);
            }
            else if (reason.Contains("Too many files"))
            {
                _logger.Warning("Skipped large ZIP: {0} ({1} files exceeds max_files: {2})", Path.GetFileName(zipPath), analysis.FileCount, _config.MaxFiles);
            }
            else if (reason.Contains("Too few files"))
            {
                _logger.Warning("Skipped empty ZIP: {0} ({1} files, min_files: {2})", Path.GetFileName(zipPath), analysis.FileCount, _config.MinFiles);
            }
            else
            {
                _logger.Warning("Skipped ZIP extraction: {0} - {1}", Path.GetFileName(zipPath), reason);
            }

            return new ZipExtractionResult
            {
                Extracted = false,
                SkipReason = analysis.SkipReason,
                Analysis = analysis
            };
        }

        // Perform extraction
        try
        {
            Directory.CreateDirectory(destinationPath);

            var extractedFiles = new List<ExtractedFileInfo>();
            var totalSize = 0L;

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Skip directories
                    if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                    {
                        continue;
                    }

                    // Double-check path safety (should already be validated)
                    if (!SecurityHelper.IsZipEntrySafe(entry.FullName))
                    {
                        _logger.Warning("Skipping unsafe entry during extraction: {0}", entry.FullName);
                        continue;
                    }

                    // Sanitize the path
                    var sanitizedPath = SanitizeZipEntryPath(entry.FullName);
                    var extractPath = Path.GetFullPath(Path.Combine(destinationPath, sanitizedPath));

                    // Verify the path is still within the destination
                    if (!extractPath.StartsWith(Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Warning("Entry would extract outside destination, skipping: {0}", entry.FullName);
                        continue;
                    }

                    // Create directory for the file
                    var entryDir = Path.GetDirectoryName(extractPath);
                    if (!string.IsNullOrEmpty(entryDir))
                    {
                        Directory.CreateDirectory(entryDir);
                    }

                    // Handle filename collisions
                    extractPath = GetUniqueFilePath(extractPath);

                    // Extract the file
                    using (var entryStream = entry.Open())
                    using (var fileStream = File.Create(extractPath))
                    {
                        await entryStream.CopyToAsync(fileStream, cancellationToken);
                    }

                    var fileInfo = new FileInfo(extractPath);
                    extractedFiles.Add(new ExtractedFileInfo
                    {
                        RelativePath = sanitizedPath,
                        ExtractedPath = extractPath,
                        SizeBytes = fileInfo.Length
                    });
                    totalSize += fileInfo.Length;
                }
            }

            _logger.Info("Extracted ZIP: {0} ({1} files, {2:N0} bytes) to {3}",
                Path.GetFileName(zipPath), extractedFiles.Count, totalSize, destinationPath);

            return new ZipExtractionResult
            {
                Extracted = true,
                ExtractionPath = destinationPath,
                FileCount = extractedFiles.Count,
                TotalSizeBytes = totalSize,
                Analysis = analysis,
                ExtractedFiles = extractedFiles
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to extract ZIP {0}: {1}", zipPath, ex.Message);
            return new ZipExtractionResult
            {
                Extracted = false,
                SkipReason = $"Extraction failed: {ex.Message}",
                Analysis = analysis
            };
        }
    }

    private static bool IsEntryEncrypted(ZipArchiveEntry entry)
    {
        // ZipArchive doesn't directly expose encryption status
        // We try to detect it by attempting to read a small amount
        try
        {
            using var stream = entry.Open();
            // Try to read one byte - encrypted entries will throw
            if (entry.Length > 0)
            {
                var buffer = new byte[1];
                stream.ReadExactly(buffer, 0, 1);
            }
            return false;
        }
        catch (InvalidDataException)
        {
            // Likely encrypted or corrupted
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string SanitizeZipEntryPath(string entryPath)
    {
        // Replace backslashes with forward slashes for consistency
        var normalized = entryPath.Replace('\\', '/');

        // Remove any leading slashes
        normalized = normalized.TrimStart('/');

        // Split into parts and sanitize each
        var parts = normalized.Split('/')
            .Select(SanitizePathPart)
            .Where(p => !string.IsNullOrEmpty(p));

        return Path.Combine(parts.ToArray());
    }

    private static string SanitizePathPart(string part)
    {
        // Skip parent directory references
        if (part == ".." || part == ".")
        {
            return "";
        }

        // Replace invalid filename characters
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
        {
            part = part.Replace(c, '_');
        }

        return part;
    }

    private static string GetUniqueFilePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? "";
        var filename = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var counter = 1;

        string newPath;
        do
        {
            newPath = Path.Combine(directory, $"{filename}_{counter}{extension}");
            counter++;
        }
        while (File.Exists(newPath));

        return newPath;
    }
}
