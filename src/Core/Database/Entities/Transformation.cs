namespace M365MailMirror.Core.Database.Entities;

/// <summary>
/// Represents a transformation applied to a message.
/// Tracks when each output format (HTML, Markdown, attachments) was generated.
/// </summary>
public class Transformation
{
    /// <summary>
    /// The Graph ID of the message this transformation belongs to.
    /// Part of composite primary key.
    /// </summary>
    public required string MessageId { get; set; }

    /// <summary>
    /// The type of transformation: "html", "markdown", or "attachments".
    /// Part of composite primary key.
    /// </summary>
    public required string TransformationType { get; set; }

    /// <summary>
    /// When this transformation was applied.
    /// </summary>
    public required DateTimeOffset AppliedAt { get; set; }

    /// <summary>
    /// A hash of the configuration settings used for this transformation.
    /// Used to detect when regeneration is needed due to config changes.
    /// </summary>
    public required string ConfigVersion { get; set; }

    /// <summary>
    /// The path to the generated output file or folder.
    /// </summary>
    public required string OutputPath { get; set; }

    /// <summary>
    /// Size of the output file in bytes.
    /// May be null for records created before schema V4 or for attachment transformations
    /// (where individual file sizes are tracked in the attachments table).
    /// </summary>
    public long? OutputSizeBytes { get; set; }
}
