namespace M365MailMirror.Core.Database.Entities;

/// <summary>
/// Represents the sync state for a mailbox.
/// Tracks last sync time, batch progress, and delta tokens for incremental sync.
/// </summary>
public class SyncState
{
    /// <summary>
    /// The mailbox identifier (email address or "me").
    /// Primary key.
    /// </summary>
    public required string Mailbox { get; set; }

    /// <summary>
    /// The timestamp of the last successful sync in ISO 8601 format.
    /// </summary>
    public required DateTimeOffset LastSyncTime { get; set; }

    /// <summary>
    /// The ID of the last completed batch during sync.
    /// Used for resumption after interruption.
    /// </summary>
    public int LastBatchId { get; set; }

    /// <summary>
    /// The delta token for Microsoft Graph delta queries.
    /// Null for initial sync, populated after first full sync.
    /// </summary>
    public string? LastDeltaToken { get; set; }

    /// <summary>
    /// When this sync state record was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When this sync state record was last updated.
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; set; }
}
