namespace M365MailMirror.Core.Storage;

/// <summary>
/// Service for storing and managing EML files in the local archive.
/// EML files are stored with date hierarchy: eml/{YYYY}/{MM}/{filename}.eml
/// </summary>
public interface IEmlStorageService
{
    /// <summary>
    /// Stores EML content to the archive using date hierarchy.
    /// Filename includes folder prefix and datetime: {folder}_{datetime}_{subject}.eml
    /// </summary>
    /// <param name="emlContent">The raw MIME content stream.</param>
    /// <param name="folderPath">The M365 folder path for filename prefix (e.g., "Inbox/Processed").</param>
    /// <param name="subject">The message subject for filename generation.</param>
    /// <param name="receivedTime">When the message was received (for date hierarchy and filename).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The relative path where the EML file was stored.</returns>
    Task<string> StoreEmlAsync(
        Stream emlContent,
        string folderPath,
        string? subject,
        DateTimeOffset receivedTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an EML file exists at the specified path.
    /// </summary>
    /// <param name="relativePath">The relative path to check.</param>
    /// <returns>True if the file exists, false otherwise.</returns>
    bool Exists(string relativePath);

    /// <summary>
    /// Deletes an EML file from the archive.
    /// </summary>
    /// <param name="relativePath">The relative path of the file to delete.</param>
    void Delete(string relativePath);

    /// <summary>
    /// Gets the full absolute path for a relative path in the archive.
    /// </summary>
    /// <param name="relativePath">The relative path.</param>
    /// <returns>The full absolute path.</returns>
    string GetFullPath(string relativePath);

    /// <summary>
    /// Opens an EML file for reading.
    /// </summary>
    /// <param name="relativePath">The relative path of the EML file.</param>
    /// <returns>A stream for reading the EML content.</returns>
    Stream OpenRead(string relativePath);

    /// <summary>
    /// Gets the size of a file in the archive.
    /// </summary>
    /// <param name="relativePath">The relative path of the file.</param>
    /// <returns>The file size in bytes.</returns>
    long GetFileSize(string relativePath);

    /// <summary>
    /// Moves an EML file to the quarantine folder, preserving its relative structure.
    /// Files are moved from eml/{path} to _Quarantine/eml/{path}.
    /// </summary>
    /// <param name="relativePath">The relative path of the EML file to quarantine.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new relative path in the quarantine folder.</returns>
    Task<string> MoveToQuarantineAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up orphaned temporary files that are older than the specified age.
    /// These can accumulate if the process crashes between temp file creation and rename.
    /// </summary>
    /// <param name="maxAge">Maximum age of temp files to keep. Files older than this are deleted.</param>
    void CleanupOrphanedTempFiles(TimeSpan maxAge);
}
