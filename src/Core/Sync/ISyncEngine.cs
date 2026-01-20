namespace M365MailMirror.Core.Sync;

/// <summary>
/// Interface for the sync engine that handles downloading and archiving messages from Microsoft 365.
/// </summary>
public interface ISyncEngine
{
    /// <summary>
    /// Performs an initial sync of all messages from the mailbox.
    /// Downloads messages in batches with checkpointing for resumption.
    /// </summary>
    /// <param name="options">Sync configuration options.</param>
    /// <param name="progressCallback">Optional callback for progress updates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the sync operation.</returns>
    Task<SyncResult> SyncAsync(
        SyncOptions options,
        SyncProgressCallback? progressCallback = null,
        CancellationToken cancellationToken = default);
}
