namespace M365MailMirror.Core.Database.Entities;

/// <summary>
/// Tracks in-progress sync state for a folder during streaming sync.
/// Created when sync starts on a folder, deleted when sync completes.
/// Enables fine-grained resumption from exact page and message position.
/// </summary>
public class FolderSyncProgress
{
    /// <summary>
    /// The Graph ID of the folder being synced.
    /// Primary key. Foreign key to folders table.
    /// </summary>
    public required string FolderId { get; set; }

    /// <summary>
    /// The nextLink URL from Microsoft Graph delta query.
    /// Used to resume pagination from the current page.
    /// Null if sync hasn't started or is on first page.
    /// </summary>
    public string? PendingNextLink { get; set; }

    /// <summary>
    /// The current page number being processed (1-based).
    /// Used for progress reporting and debugging.
    /// </summary>
    public int PendingPageNumber { get; set; }

    /// <summary>
    /// The index of the last successfully processed message within the current page.
    /// Used to skip already-processed messages when resuming mid-page.
    /// </summary>
    public int PendingMessageIndex { get; set; }

    /// <summary>
    /// When sync started for this folder.
    /// </summary>
    public DateTimeOffset? SyncStartedAt { get; set; }

    /// <summary>
    /// When the last checkpoint was saved.
    /// </summary>
    public DateTimeOffset? LastCheckpointAt { get; set; }

    /// <summary>
    /// Total number of messages processed so far in this folder.
    /// </summary>
    public int MessagesProcessed { get; set; }
}
