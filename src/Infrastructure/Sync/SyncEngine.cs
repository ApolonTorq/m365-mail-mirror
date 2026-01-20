using System.Diagnostics;
using M365MailMirror.Core.Database;
using M365MailMirror.Core.Database.Entities;
using M365MailMirror.Core.Graph;
using M365MailMirror.Core.Logging;
using M365MailMirror.Core.Storage;
using M365MailMirror.Core.Sync;

namespace M365MailMirror.Infrastructure.Sync;

/// <summary>
/// Sync engine for downloading and archiving messages from Microsoft 365.
/// Implements batch processing with checkpointing for reliable resumption.
/// </summary>
public class SyncEngine : ISyncEngine
{
    private readonly IGraphMailClient _graphClient;
    private readonly IStateDatabase _database;
    private readonly IEmlStorageService _emlStorage;
    private readonly IAppLogger _logger;

    /// <summary>
    /// Default overlap period in minutes for date-based fallback queries.
    /// This catches messages that arrived late or were delayed.
    /// </summary>
    private const int DefaultOverlapMinutes = 60;

    /// <summary>
    /// Creates a new SyncEngine instance.
    /// </summary>
    public SyncEngine(
        IGraphMailClient graphClient,
        IStateDatabase database,
        IEmlStorageService emlStorage,
        IAppLogger? logger = null)
    {
        _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _emlStorage = emlStorage ?? throw new ArgumentNullException(nameof(emlStorage));
        _logger = logger ?? LoggerFactory.CreateLogger<SyncEngine>();
    }

    /// <inheritdoc />
    public async Task<SyncResult> SyncAsync(
        SyncOptions options,
        SyncProgressCallback? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var messagesSynced = 0;
        var messagesSkipped = 0;
        var foldersProcessed = 0;
        var errors = 0;

        try
        {
            _logger.Info("Starting sync operation (dryRun: {0}, batchSize: {1})", options.DryRun, options.BatchSize);

            // Phase 1: Get mailbox identifier
            var mailbox = options.Mailbox ?? await _graphClient.GetUserEmailAsync(cancellationToken);
            _logger.Info("Syncing mailbox: {0}", mailbox);

            // Phase 2: Get or create sync state
            var syncState = await GetOrCreateSyncStateAsync(mailbox, options.DryRun, cancellationToken);
            _logger.Debug("Starting from batch {0}", syncState.LastBatchId);

            // Phase 3: Enumerate folders
            progressCallback?.Invoke(new SyncProgress
            {
                Phase = "Enumerating folders",
                TotalMessagesSynced = messagesSynced
            });

            var folders = await _graphClient.GetFoldersAsync(options.Mailbox, cancellationToken);
            var filteredFolders = FilterFolders(folders, options.ExcludeFolders);

            _logger.Info("Found {0} folders to sync (excluding {1})", filteredFolders.Count, folders.Count - filteredFolders.Count);

            // Phase 4: Store folder mappings
            if (!options.DryRun)
            {
                await StoreFolderMappingsAsync(filteredFolders, cancellationToken);
            }

            // Phase 5: Process each folder
            var totalFolders = filteredFolders.Count;
            foreach (var folder in filteredFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progressCallback?.Invoke(new SyncProgress
                {
                    Phase = "Syncing folder",
                    CurrentFolder = folder.FullPath,
                    TotalFolders = totalFolders,
                    ProcessedFolders = foldersProcessed,
                    TotalMessagesInFolder = folder.TotalItemCount,
                    TotalMessagesSynced = messagesSynced
                });

                _logger.Info("Processing folder: {0} ({1} messages)", folder.FullPath, folder.TotalItemCount);

                var (synced, skipped, folderErrors) = await ProcessFolderAsync(
                    folder,
                    mailbox,
                    syncState,
                    options,
                    progressCallback,
                    cancellationToken);

                messagesSynced += synced;
                messagesSkipped += skipped;
                errors += folderErrors;
                foldersProcessed++;

                _logger.Info("Completed folder {0}: {1} synced, {2} skipped, {3} errors",
                    folder.FullPath, synced, skipped, folderErrors);
            }

            // Phase 6: Update final sync state
            if (!options.DryRun)
            {
                syncState.LastSyncTime = DateTimeOffset.UtcNow;
                syncState.UpdatedAt = DateTimeOffset.UtcNow;
                await _database.UpsertSyncStateAsync(syncState, cancellationToken);
            }

            stopwatch.Stop();
            _logger.Info("Sync completed: {0} messages synced, {1} skipped, {2} folders, {3} errors, elapsed: {4}",
                messagesSynced, messagesSkipped, foldersProcessed, errors, stopwatch.Elapsed);

            return SyncResult.Successful(messagesSynced, messagesSkipped, foldersProcessed, errors, stopwatch.Elapsed, options.DryRun);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.Warning("Sync cancelled after {0}", stopwatch.Elapsed);
            return SyncResult.Failed("Sync was cancelled", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.Error(ex, "Sync failed: {0}", ex.Message);
            return SyncResult.Failed(ex.Message, stopwatch.Elapsed);
        }
    }

    private async Task<SyncState> GetOrCreateSyncStateAsync(string mailbox, bool dryRun, CancellationToken cancellationToken)
    {
        var syncState = await _database.GetSyncStateAsync(mailbox, cancellationToken);

        if (syncState == null)
        {
            syncState = new SyncState
            {
                Mailbox = mailbox,
                LastSyncTime = DateTimeOffset.MinValue,
                LastBatchId = 0,
                LastDeltaToken = null,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            if (!dryRun)
            {
                await _database.UpsertSyncStateAsync(syncState, cancellationToken);
            }
        }

        return syncState;
    }

    private static List<AppMailFolder> FilterFolders(IReadOnlyList<AppMailFolder> folders, IReadOnlyList<string> excludeFolders)
    {
        if (excludeFolders.Count == 0)
        {
            return folders.ToList();
        }

        return folders
            .Where(f => !excludeFolders.Any(exclude =>
                f.DisplayName.Equals(exclude, StringComparison.OrdinalIgnoreCase) ||
                f.FullPath.Equals(exclude, StringComparison.OrdinalIgnoreCase) ||
                f.FullPath.StartsWith(exclude + "/", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private async Task StoreFolderMappingsAsync(List<AppMailFolder> folders, CancellationToken cancellationToken)
    {
        foreach (var folder in folders)
        {
            var folderEntity = new Folder
            {
                GraphId = folder.Id,
                ParentFolderId = folder.ParentFolderId,
                LocalPath = folder.FullPath,
                DisplayName = folder.DisplayName,
                TotalItemCount = folder.TotalItemCount,
                UnreadItemCount = folder.UnreadItemCount,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await _database.UpsertFolderAsync(folderEntity, cancellationToken);
        }
    }

    private async Task<(int synced, int skipped, int errors)> ProcessFolderAsync(
        AppMailFolder folder,
        string mailbox,
        SyncState syncState,
        SyncOptions options,
        SyncProgressCallback? progressCallback,
        CancellationToken cancellationToken)
    {
        var synced = 0;
        var skipped = 0;
        var errors = 0;
        var batchId = 0;
        var processedInFolder = 0;

        // Load stored folder state for incremental sync
        var storedFolder = await _database.GetFolderAsync(folder.Id, cancellationToken);
        var storedDeltaToken = storedFolder?.DeltaToken;
        var storedLastSyncTime = storedFolder?.LastSyncTime;

        string? finalDeltaToken = null;
        var usedDateFallback = false;

        try
        {
            // Try delta query first (with stored token for incremental sync)
            var (newMessages, movedMessages, deletedMessages, newDeltaToken) = await GetMessagesWithDeltaAsync(
                folder,
                storedDeltaToken,
                options,
                cancellationToken);

            finalDeltaToken = newDeltaToken;

            // Process deleted messages first (quarantine them)
            if (deletedMessages.Count > 0)
            {
                _logger.Info("Processing {0} deleted messages from folder {1}", deletedMessages.Count, folder.FullPath);
                var deletedErrors = await ProcessDeletedMessagesAsync(deletedMessages, options, cancellationToken);
                errors += deletedErrors;
            }

            // Process moved messages (relocate existing files)
            if (movedMessages.Count > 0)
            {
                _logger.Info("Processing {0} moved messages from folder {1}", movedMessages.Count, folder.FullPath);
                var movedErrors = await ProcessMovedMessagesAsync(movedMessages, options, cancellationToken);
                errors += movedErrors;
            }

            // Process new/updated messages in sub-batches for checkpointing
            var (s, sk, e, bid, proc) = await ProcessMessagesInBatchesAsync(
                newMessages,
                folder,
                mailbox,
                syncState,
                options,
                progressCallback,
                batchId,
                processedInFolder,
                cancellationToken);

            synced += s;
            skipped += sk;
            errors += e;
            batchId = bid;
            processedInFolder = proc;
        }
        catch (Exception ex) when (ShouldUseDateFallback(ex))
        {
            // Delta token expired or resync required - fall back to date-based query
            _logger.Warning("Delta query failed for folder {0}, falling back to date-based sync: {1}",
                folder.FullPath, ex.Message);

            usedDateFallback = true;

            if (storedLastSyncTime.HasValue)
            {
                // Use date-based fallback with overlap
                var sinceDate = storedLastSyncTime.Value.AddMinutes(-DefaultOverlapMinutes);
                var dateMessages = await _graphClient.GetMessagesSinceDateAsync(
                    folder.Id,
                    sinceDate,
                    options.Mailbox,
                    cancellationToken);

                _logger.Info("Date-based fallback returned {0} messages since {1}", dateMessages.Count, sinceDate);

                var (s, sk, e, bid, proc) = await ProcessMessagesInBatchesAsync(
                    dateMessages.ToList(),
                    folder,
                    mailbox,
                    syncState,
                    options,
                    progressCallback,
                    batchId,
                    processedInFolder,
                    cancellationToken);

                synced += s;
                skipped += sk;
                errors += e;
                batchId = bid;
                processedInFolder = proc;
            }
            else
            {
                // No previous sync time - need full resync without delta token
                var (newMessages, movedMessages, deletedMessages, newDeltaToken) = await GetMessagesWithDeltaAsync(
                    folder,
                    null, // Force full sync
                    options,
                    cancellationToken);

                finalDeltaToken = newDeltaToken;

                // Process deleted messages first (quarantine them)
                if (deletedMessages.Count > 0)
                {
                    _logger.Info("Processing {0} deleted messages from folder {1}", deletedMessages.Count, folder.FullPath);
                    var deletedErrors = await ProcessDeletedMessagesAsync(deletedMessages, options, cancellationToken);
                    errors += deletedErrors;
                }

                // Process moved messages
                if (movedMessages.Count > 0)
                {
                    _logger.Info("Processing {0} moved messages from folder {1}", movedMessages.Count, folder.FullPath);
                    var movedErrors = await ProcessMovedMessagesAsync(movedMessages, options, cancellationToken);
                    errors += movedErrors;
                }

                var (s, sk, e, bid, proc) = await ProcessMessagesInBatchesAsync(
                    newMessages,
                    folder,
                    mailbox,
                    syncState,
                    options,
                    progressCallback,
                    batchId,
                    processedInFolder,
                    cancellationToken);

                synced += s;
                skipped += sk;
                errors += e;
                batchId = bid;
                processedInFolder = proc;
            }
        }

        // Update folder's delta token and last sync time (if not dry run)
        if (!options.DryRun)
        {
            var folderToUpdate = storedFolder ?? new Folder
            {
                GraphId = folder.Id,
                ParentFolderId = folder.ParentFolderId,
                LocalPath = folder.FullPath,
                DisplayName = folder.DisplayName,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            // Only update delta token if we used delta query (not date fallback)
            if (!usedDateFallback && finalDeltaToken != null)
            {
                folderToUpdate.DeltaToken = finalDeltaToken;
            }

            folderToUpdate.LastSyncTime = DateTimeOffset.UtcNow;
            folderToUpdate.TotalItemCount = folder.TotalItemCount;
            folderToUpdate.UnreadItemCount = folder.UnreadItemCount;
            folderToUpdate.UpdatedAt = DateTimeOffset.UtcNow;

            await _database.UpsertFolderAsync(folderToUpdate, cancellationToken);
        }

        return (synced, skipped, errors);
    }

    private async Task<(List<MessageInfo> newMessages, List<MessageInfo> movedMessages, List<MessageInfo> deletedMessages, string? deltaToken)> GetMessagesWithDeltaAsync(
        AppMailFolder folder,
        string? storedDeltaToken,
        SyncOptions options,
        CancellationToken cancellationToken)
    {
        var newMessages = new List<MessageInfo>();
        var movedMessages = new List<MessageInfo>();
        var deletedMessages = new List<MessageInfo>();
        string? deltaToken = storedDeltaToken;
        string? finalDeltaToken = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _graphClient.GetMessagesDeltaAsync(
                folder.Id,
                deltaToken,
                options.Mailbox,
                cancellationToken);

            _logger.Debug("Received batch of {0} messages from folder {1}", result.Items.Count, folder.FullPath);

            foreach (var message in result.Items)
            {
                if (message.IsDeleted)
                {
                    // Message was deleted from Microsoft 365
                    deletedMessages.Add(message);
                }
                else if (message.IsMoved)
                {
                    // Message was moved out of this folder
                    movedMessages.Add(message);
                }
                else
                {
                    // New or updated message
                    newMessages.Add(message);
                }
            }

            if (result.HasMorePages)
            {
                deltaToken = result.NextPageLink;
            }
            else
            {
                finalDeltaToken = result.DeltaToken;
                break;
            }

        } while (true);

        return (newMessages, movedMessages, deletedMessages, finalDeltaToken);
    }

    private async Task<int> ProcessMovedMessagesAsync(
        List<MessageInfo> movedMessages,
        SyncOptions options,
        CancellationToken cancellationToken)
    {
        var errors = 0;

        foreach (var messageInfo in movedMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Look up the message in the database by immutable ID
                var immutableId = messageInfo.ImmutableId ?? messageInfo.Id;
                var existingMessage = await _database.GetMessageByImmutableIdAsync(immutableId, cancellationToken);

                if (existingMessage == null)
                {
                    // Message doesn't exist in our database - nothing to move
                    _logger.Debug("Moved message {0} not found in database, skipping", immutableId);
                    continue;
                }

                if (messageInfo.NewParentFolderId == null)
                {
                    _logger.Warning("Moved message {0} has no new parent folder ID", immutableId);
                    continue;
                }

                // Get the new folder path
                var newFolder = await _database.GetFolderAsync(messageInfo.NewParentFolderId, cancellationToken);
                if (newFolder == null)
                {
                    // New folder not yet in database - will be discovered when we sync that folder
                    _logger.Debug("New folder {0} not found in database for moved message {1}", messageInfo.NewParentFolderId, immutableId);
                    continue;
                }

                var newFolderPath = newFolder.LocalPath;
                var oldFolderPath = existingMessage.FolderPath;

                if (string.Equals(newFolderPath, oldFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    // Same folder - no move needed
                    continue;
                }

                if (options.DryRun)
                {
                    _logger.Info("Would move message {0} from {1} to {2}",
                        existingMessage.Subject ?? existingMessage.ImmutableId,
                        oldFolderPath,
                        newFolderPath);
                    continue;
                }

                // Move the EML file to the new folder
                var newLocalPath = await _emlStorage.MoveEmlAsync(
                    existingMessage.LocalPath,
                    newFolderPath,
                    cancellationToken);

                // Update the database
                existingMessage.LocalPath = newLocalPath;
                existingMessage.FolderPath = newFolderPath;
                existingMessage.UpdatedAt = DateTimeOffset.UtcNow;

                await _database.UpdateMessageAsync(existingMessage, cancellationToken);

                _logger.Info("Moved message {0} from {1} to {2}",
                    existingMessage.Subject ?? existingMessage.ImmutableId,
                    oldFolderPath,
                    newFolderPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing moved message {0}: {1}", messageInfo.Id, ex.Message);
                errors++;
            }
        }

        return errors;
    }

    private async Task<int> ProcessDeletedMessagesAsync(
        List<MessageInfo> deletedMessages,
        SyncOptions options,
        CancellationToken cancellationToken)
    {
        var errors = 0;

        foreach (var messageInfo in deletedMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Look up the message in the database by immutable ID or Graph ID
                var immutableId = messageInfo.ImmutableId ?? messageInfo.Id;
                var existingMessage = await _database.GetMessageByImmutableIdAsync(immutableId, cancellationToken);

                // Also try by Graph ID if not found by immutable ID
                if (existingMessage == null)
                {
                    existingMessage = await _database.GetMessageAsync(messageInfo.Id, cancellationToken);
                }

                if (existingMessage == null)
                {
                    // Message doesn't exist in our database - nothing to quarantine
                    _logger.Debug("Deleted message {0} not found in database, skipping", immutableId);
                    continue;
                }

                if (existingMessage.QuarantinedAt != null)
                {
                    // Already quarantined
                    _logger.Debug("Message {0} is already quarantined, skipping", immutableId);
                    continue;
                }

                if (options.DryRun)
                {
                    _logger.Info("Would quarantine message {0}: {1}",
                        existingMessage.Subject ?? existingMessage.ImmutableId,
                        existingMessage.LocalPath);
                    continue;
                }

                // Move the EML file to quarantine
                string newLocalPath;
                try
                {
                    newLocalPath = await _emlStorage.MoveToQuarantineAsync(
                        existingMessage.LocalPath,
                        cancellationToken);
                }
                catch (FileNotFoundException)
                {
                    // File already gone - just update database
                    _logger.Warning("EML file not found for deleted message {0}, updating database only", immutableId);
                    newLocalPath = existingMessage.LocalPath; // Keep original path in record
                }

                // Update the database
                existingMessage.LocalPath = newLocalPath;
                existingMessage.QuarantinedAt = DateTimeOffset.UtcNow;
                existingMessage.QuarantineReason = "deleted_in_m365";
                existingMessage.UpdatedAt = DateTimeOffset.UtcNow;

                await _database.UpdateMessageAsync(existingMessage, cancellationToken);

                _logger.Info("Quarantined deleted message {0}: {1}",
                    existingMessage.Subject ?? existingMessage.ImmutableId,
                    newLocalPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing deleted message {0}: {1}", messageInfo.Id, ex.Message);
                errors++;
            }
        }

        return errors;
    }

    private async Task<(int synced, int skipped, int errors, int batchId, int processed)> ProcessMessagesInBatchesAsync(
        List<MessageInfo> messages,
        AppMailFolder folder,
        string mailbox,
        SyncState syncState,
        SyncOptions options,
        SyncProgressCallback? progressCallback,
        int startBatchId,
        int startProcessed,
        CancellationToken cancellationToken)
    {
        var synced = 0;
        var skipped = 0;
        var errors = 0;
        var batchId = startBatchId;
        var processedInFolder = startProcessed;

        for (var i = 0; i < messages.Count; i += options.BatchSize)
        {
            var batch = messages.Skip(i).Take(options.BatchSize).ToList();
            batchId++;

            progressCallback?.Invoke(new SyncProgress
            {
                Phase = "Downloading messages",
                CurrentFolder = folder.FullPath,
                TotalMessagesInFolder = folder.TotalItemCount,
                ProcessedMessagesInFolder = processedInFolder,
                TotalMessagesSynced = synced + skipped,
                CurrentBatch = batchId
            });

            var (batchSynced, batchSkipped, batchErrors) = await ProcessBatchAsync(
                batch,
                folder,
                mailbox,
                options,
                cancellationToken);

            synced += batchSynced;
            skipped += batchSkipped;
            errors += batchErrors;
            processedInFolder += batch.Count;

            // Checkpoint after each batch (update sync state)
            if (!options.DryRun)
            {
                syncState.LastBatchId = batchId;
                syncState.UpdatedAt = DateTimeOffset.UtcNow;
                await _database.UpsertSyncStateAsync(syncState, cancellationToken);
                _logger.Debug("Checkpoint: batch {0} completed", batchId);
            }
        }

        return (synced, skipped, errors, batchId, processedInFolder);
    }

    private static bool ShouldUseDateFallback(Exception ex)
    {
        // Check for common indicators that delta token is invalid
        // Microsoft Graph returns specific errors when resync is needed
        var message = ex.Message.ToUpperInvariant();
        return message.Contains("RESYNC") ||
               message.Contains("DELTA") ||
               message.Contains("SYNC_STATE") ||
               message.Contains("TOKEN") && (message.Contains("INVALID") || message.Contains("EXPIRED"));
    }

    private async Task<(int synced, int skipped, int errors)> ProcessBatchAsync(
        List<MessageInfo> messages,
        AppMailFolder folder,
        string mailbox,
        SyncOptions options,
        CancellationToken cancellationToken)
    {
        var synced = 0;
        var skipped = 0;
        var errors = 0;

        // Use semaphore for controlled parallelism
        using var semaphore = new SemaphoreSlim(options.MaxParallelDownloads);
        var tasks = messages.Select(async message =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await ProcessMessageAsync(message, folder, mailbox, options, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);

        foreach (var result in results)
        {
            switch (result)
            {
                case MessageProcessResult.Synced:
                    synced++;
                    break;
                case MessageProcessResult.Skipped:
                    skipped++;
                    break;
                case MessageProcessResult.Error:
                    errors++;
                    break;
            }
        }

        return (synced, skipped, errors);
    }

    private async Task<MessageProcessResult> ProcessMessageAsync(
        MessageInfo messageInfo,
        AppMailFolder folder,
        string mailbox,
        SyncOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if message already exists (by immutable ID)
            var immutableId = messageInfo.ImmutableId ?? messageInfo.Id;
            var existingMessage = await _database.GetMessageByImmutableIdAsync(immutableId, cancellationToken);

            if (existingMessage != null)
            {
                _logger.Debug("Skipping message {0} (already exists)", messageInfo.Subject ?? messageInfo.Id);
                return MessageProcessResult.Skipped;
            }

            if (options.DryRun)
            {
                _logger.Debug("Would sync message: {0}", messageInfo.Subject ?? messageInfo.Id);
                return MessageProcessResult.Synced;
            }

            // Download MIME content
            using var mimeStream = await _graphClient.DownloadMessageMimeAsync(
                messageInfo.Id,
                options.Mailbox,
                cancellationToken);

            // Store EML file
            var localPath = await _emlStorage.StoreEmlAsync(
                mimeStream,
                folder.FullPath,
                messageInfo.Subject,
                messageInfo.ReceivedDateTime,
                cancellationToken);

            // Get file size
            var fileSize = _emlStorage.GetFileSize(localPath);

            // Record in database
            var message = new Message
            {
                GraphId = messageInfo.Id,
                ImmutableId = immutableId,
                LocalPath = localPath,
                FolderPath = folder.FullPath,
                Subject = messageInfo.Subject,
                Sender = messageInfo.From,
                Recipients = null, // Would need additional API call to get recipients
                ReceivedTime = messageInfo.ReceivedDateTime,
                Size = fileSize,
                HasAttachments = messageInfo.HasAttachments,
                InReplyTo = null, // Would parse from EML headers if needed
                ConversationId = null, // Would need additional API call
                QuarantinedAt = null,
                QuarantineReason = null,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await _database.InsertMessageAsync(message, cancellationToken);

            _logger.Debug("Synced message: {0}", messageInfo.Subject ?? messageInfo.Id);
            return MessageProcessResult.Synced;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing message {0}: {1}", messageInfo.Id, ex.Message);
            return MessageProcessResult.Error;
        }
    }

    private enum MessageProcessResult
    {
        Synced,
        Skipped,
        Error
    }
}
