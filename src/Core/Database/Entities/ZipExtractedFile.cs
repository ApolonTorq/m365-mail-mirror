namespace M365MailMirror.Core.Database.Entities;

/// <summary>
/// Represents an individual file extracted from a ZIP archive.
/// </summary>
public class ZipExtractedFile
{
    /// <summary>
    /// Auto-incrementing primary key.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The ID of the ZIP extraction this file belongs to.
    /// </summary>
    public required long ZipExtractionId { get; set; }

    /// <summary>
    /// The relative path within the ZIP archive.
    /// Example: "data/report.csv"
    /// </summary>
    public required string RelativePath { get; set; }

    /// <summary>
    /// The full path to the extracted file on disk.
    /// </summary>
    public required string ExtractedPath { get; set; }

    /// <summary>
    /// The file size in bytes.
    /// </summary>
    public required long SizeBytes { get; set; }
}
