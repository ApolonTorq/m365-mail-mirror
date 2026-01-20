namespace M365MailMirror.Core.Database.Entities;

/// <summary>
/// Represents an attachment extracted from a message.
/// </summary>
public class Attachment
{
    /// <summary>
    /// Auto-incrementing primary key.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The Graph ID of the message this attachment belongs to.
    /// </summary>
    public required string MessageId { get; set; }

    /// <summary>
    /// The original filename of the attachment.
    /// </summary>
    public required string Filename { get; set; }

    /// <summary>
    /// The relative path to the extracted attachment file from the archive root.
    /// </summary>
    public required string FilePath { get; set; }

    /// <summary>
    /// The file size in bytes.
    /// </summary>
    public required long SizeBytes { get; set; }

    /// <summary>
    /// The MIME content type of the attachment.
    /// Example: "application/pdf", "image/png"
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Whether this is an inline attachment (embedded in message body)
    /// as opposed to a regular attachment.
    /// </summary>
    public required bool IsInline { get; set; }

    /// <summary>
    /// Whether extraction was skipped for this attachment.
    /// True for blocked file types (executables, etc.)
    /// </summary>
    public bool Skipped { get; set; }

    /// <summary>
    /// The reason extraction was skipped.
    /// Example: "executable", "encrypted"
    /// </summary>
    public string? SkipReason { get; set; }

    /// <summary>
    /// When this attachment was extracted.
    /// </summary>
    public required DateTimeOffset ExtractedAt { get; set; }
}
