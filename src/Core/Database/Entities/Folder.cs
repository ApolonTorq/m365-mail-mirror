namespace M365MailMirror.Core.Database.Entities;

/// <summary>
/// Represents a mail folder mapping between Graph API and local storage.
/// </summary>
public class Folder
{
    /// <summary>
    /// The Graph ID of the folder.
    /// Primary key.
    /// </summary>
    public required string GraphId { get; set; }

    /// <summary>
    /// The Graph ID of the parent folder, if any.
    /// Null for top-level folders.
    /// </summary>
    public string? ParentFolderId { get; set; }

    /// <summary>
    /// The local path for this folder relative to the archive root.
    /// Example: "Inbox/Subfolder"
    /// </summary>
    public required string LocalPath { get; set; }

    /// <summary>
    /// The display name of the folder as shown in Outlook.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// The total number of items in the folder, if known.
    /// </summary>
    public int? TotalItemCount { get; set; }

    /// <summary>
    /// The number of unread items in the folder, if known.
    /// </summary>
    public int? UnreadItemCount { get; set; }

    /// <summary>
    /// The delta token for incremental sync of this folder.
    /// Used for efficient delta queries to get only changed messages.
    /// </summary>
    public string? DeltaToken { get; set; }

    /// <summary>
    /// When this folder was last synced.
    /// Used for date-based fallback if delta token is invalid.
    /// </summary>
    public DateTimeOffset? LastSyncTime { get; set; }

    /// <summary>
    /// When this folder record was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When this folder record was last updated.
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; set; }
}
