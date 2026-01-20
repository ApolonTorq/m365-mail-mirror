namespace M365MailMirror.Core.Database.Entities;

/// <summary>
/// Represents a ZIP file extraction attempt.
/// Tracks whether extraction was performed and why it may have been skipped.
/// </summary>
public class ZipExtraction
{
    /// <summary>
    /// Auto-incrementing primary key.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The ID of the attachment record this ZIP belongs to.
    /// </summary>
    public required long AttachmentId { get; set; }

    /// <summary>
    /// The Graph ID of the message this ZIP belongs to.
    /// </summary>
    public required string MessageId { get; set; }

    /// <summary>
    /// The ZIP filename.
    /// </summary>
    public required string ZipFilename { get; set; }

    /// <summary>
    /// The path to the extraction folder ({filename}.zip_extracted/).
    /// </summary>
    public required string ExtractionPath { get; set; }

    /// <summary>
    /// Whether the ZIP contents were extracted.
    /// False if extraction was skipped for any reason.
    /// </summary>
    public required bool Extracted { get; set; }

    /// <summary>
    /// The reason extraction was skipped, if applicable.
    /// Example: "encrypted", "too_many_files", "contains_executables", "unsafe_paths"
    /// </summary>
    public string? SkipReason { get; set; }

    /// <summary>
    /// The number of files in the ZIP archive.
    /// </summary>
    public int? FileCount { get; set; }

    /// <summary>
    /// The total uncompressed size in bytes.
    /// </summary>
    public long? TotalSizeBytes { get; set; }

    /// <summary>
    /// Whether the ZIP contains executable files.
    /// </summary>
    public bool? HasExecutables { get; set; }

    /// <summary>
    /// Whether the ZIP contains unsafe paths (absolute paths, path traversal).
    /// </summary>
    public bool? HasUnsafePaths { get; set; }

    /// <summary>
    /// Whether the ZIP is password-protected.
    /// </summary>
    public bool? IsEncrypted { get; set; }

    /// <summary>
    /// When the extraction was performed or attempted.
    /// </summary>
    public required DateTimeOffset ExtractedAt { get; set; }
}
