namespace M365MailMirror.Core.Storage;

/// <summary>
/// Service for storing and managing EML files in the local archive.
/// EML files are stored with folder/date hierarchy: eml/{folder}/{YYYY}/{MM}/{filename}.eml
/// </summary>
public interface IEmlStorageService
{
    /// <summary>
    /// Stores EML content to the archive using folder/date hierarchy.
    /// </summary>
    /// <param name="emlContent">The raw MIME content stream.</param>
    /// <param name="folderPath">The mail folder path (e.g., "Inbox/Important").</param>
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
    /// Moves an EML file from one location to another (e.g., for folder moves or quarantine).
    /// </summary>
    /// <param name="sourcePath">The current relative path of the EML file.</param>
    /// <param name="destinationFolderPath">The destination folder path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new relative path of the EML file.</returns>
    Task<string> MoveEmlAsync(
        string sourcePath,
        string destinationFolderPath,
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
}
