namespace M365MailMirror.Core.Database.Entities;

/// <summary>
/// Represents an email message stored in the archive.
/// Stores metadata only - content is in the EML file.
/// </summary>
public class Message
{
    /// <summary>
    /// The mutable Graph ID for the message.
    /// Primary key. May change if message moves between folders.
    /// </summary>
    public required string GraphId { get; set; }

    /// <summary>
    /// The immutable ID from Microsoft Graph.
    /// Stable across folder moves and other changes.
    /// </summary>
    public required string ImmutableId { get; set; }

    /// <summary>
    /// The relative path to the EML file from the archive root.
    /// Example: "eml/Inbox/2024/01/Meeting_Notes_1030.eml"
    /// </summary>
    public required string LocalPath { get; set; }

    /// <summary>
    /// The folder path where the message is stored.
    /// Example: "Inbox" or "Inbox/Subfolder"
    /// </summary>
    public required string FolderPath { get; set; }

    /// <summary>
    /// The message subject line.
    /// May be null for messages without a subject.
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// The sender email address.
    /// </summary>
    public string? Sender { get; set; }

    /// <summary>
    /// The recipient email addresses as a JSON array.
    /// Example: ["user@example.com", "other@example.com"]
    /// </summary>
    public string? Recipients { get; set; }

    /// <summary>
    /// When the message was received.
    /// </summary>
    public required DateTimeOffset ReceivedTime { get; set; }

    /// <summary>
    /// The message size in bytes.
    /// </summary>
    public required long Size { get; set; }

    /// <summary>
    /// Whether the message has attachments.
    /// </summary>
    public required bool HasAttachments { get; set; }

    /// <summary>
    /// The Message-ID header value for threading.
    /// Used to link replies to original messages.
    /// </summary>
    public string? InReplyTo { get; set; }

    /// <summary>
    /// The conversation ID for threading messages.
    /// </summary>
    public string? ConversationId { get; set; }

    /// <summary>
    /// When the message was moved to quarantine, if applicable.
    /// Null if not quarantined.
    /// </summary>
    public DateTimeOffset? QuarantinedAt { get; set; }

    /// <summary>
    /// The reason for quarantine.
    /// Example: "deleted_in_m365", "user_request"
    /// </summary>
    public string? QuarantineReason { get; set; }

    /// <summary>
    /// When this message record was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When this message record was last updated.
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; set; }
}
