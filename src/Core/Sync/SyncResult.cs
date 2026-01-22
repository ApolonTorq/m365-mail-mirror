namespace M365MailMirror.Core.Sync;

/// <summary>
/// Result of a sync operation.
/// </summary>
public class SyncResult
{
    /// <summary>
    /// Whether the sync completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Number of messages synced.
    /// </summary>
    public int MessagesSynced { get; init; }

    /// <summary>
    /// Number of messages skipped (already present).
    /// </summary>
    public int MessagesSkipped { get; init; }

    /// <summary>
    /// Number of folders processed.
    /// </summary>
    public int FoldersProcessed { get; init; }

    /// <summary>
    /// Number of errors encountered.
    /// </summary>
    public int Errors { get; init; }

    /// <summary>
    /// Total time elapsed for the sync operation.
    /// </summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>
    /// Error message if the sync failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Whether this was a dry run (no changes made).
    /// </summary>
    public bool IsDryRun { get; init; }

    /// <summary>
    /// Creates a successful sync result.
    /// </summary>
    public static SyncResult Successful(int messagesSynced, int messagesSkipped, int foldersProcessed, int errors, TimeSpan elapsed, bool isDryRun = false) => new()
    {
        Success = true,
        MessagesSynced = messagesSynced,
        MessagesSkipped = messagesSkipped,
        FoldersProcessed = foldersProcessed,
        Errors = errors,
        Elapsed = elapsed,
        IsDryRun = isDryRun
    };

    /// <summary>
    /// Creates a failed sync result.
    /// </summary>
    public static SyncResult Failed(string errorMessage, TimeSpan elapsed) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        Elapsed = elapsed
    };
}

/// <summary>
/// Options for configuring sync behavior.
/// </summary>
public class SyncOptions
{
    /// <summary>
    /// Number of messages after which to checkpoint progress during streaming sync.
    /// Lower values provide finer recovery granularity but more database writes.
    /// Default is 10.
    /// </summary>
    public int CheckpointInterval { get; init; } = 10;

    /// <summary>
    /// Maximum number of parallel download operations. Default is 4.
    /// </summary>
    public int MaxParallelDownloads { get; init; } = 4;

    /// <summary>
    /// Folders to exclude from sync (by display name or path).
    /// </summary>
    public IReadOnlyList<string> ExcludeFolders { get; init; } = [];

    /// <summary>
    /// Whether to perform a dry run (no files written, no database changes).
    /// </summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// The mailbox to sync. If null, uses the authenticated user's mailbox.
    /// </summary>
    public string? Mailbox { get; init; }

    /// <summary>
    /// Whether to generate HTML transformations for synced messages.
    /// </summary>
    public bool GenerateHtml { get; init; }

    /// <summary>
    /// Whether to generate Markdown transformations for synced messages.
    /// </summary>
    public bool GenerateMarkdown { get; init; }

    /// <summary>
    /// Whether to extract attachments from synced messages.
    /// </summary>
    public bool ExtractAttachments { get; init; }
}

/// <summary>
/// Progress callback delegate for sync operations.
/// </summary>
/// <param name="progress">The current sync progress.</param>
public delegate void SyncProgressCallback(SyncProgress progress);

/// <summary>
/// Represents the current progress of a sync operation.
/// </summary>
public class SyncProgress
{
    /// <summary>
    /// Current phase of the sync operation.
    /// </summary>
    public required string Phase { get; init; }

    /// <summary>
    /// Current folder being processed.
    /// </summary>
    public string? CurrentFolder { get; init; }

    /// <summary>
    /// Total number of folders to process.
    /// </summary>
    public int TotalFolders { get; init; }

    /// <summary>
    /// Number of folders processed so far.
    /// </summary>
    public int ProcessedFolders { get; init; }

    /// <summary>
    /// Total number of messages in the current folder.
    /// </summary>
    public int TotalMessagesInFolder { get; init; }

    /// <summary>
    /// Number of messages processed in the current folder.
    /// </summary>
    public int ProcessedMessagesInFolder { get; init; }

    /// <summary>
    /// Total messages synced across all folders.
    /// </summary>
    public int TotalMessagesSynced { get; init; }

    /// <summary>
    /// Current page number being processed (for streaming sync).
    /// </summary>
    public int CurrentPage { get; init; }

    /// <summary>
    /// Messages processed in current page.
    /// </summary>
    public int MessagesInCurrentPage { get; init; }
}
