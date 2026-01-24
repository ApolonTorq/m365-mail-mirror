using System.Diagnostics;
using M365MailMirror.Core.Database;
using M365MailMirror.Core.Database.Entities;
using M365MailMirror.Core.Graph;
using M365MailMirror.Core.Logging;
using M365MailMirror.Core.Storage;
using M365MailMirror.Core.Sync;
using M365MailMirror.Core.Transform;

namespace M365MailMirror.Infrastructure.Sync;

/// <summary>
/// Sync engine for downloading and archiving messages from Microsoft 365.
/// Implements streaming sync with per-message checkpointing for reliable resumption.
/// </summary>
public class SyncEngine : ISyncEngine
{
    private readonly IGraphMailClient _graphClient;
    private readonly IStateDatabase _database;
    private readonly IEmlStorageService _emlStorage;
    private readonly IAppLogger _logger;
    private readonly ITransformationService? _transformationService;

    /// <summary>
    /// Default overlap period in minutes for date-based fallback queries.
    /// This catches messages that arrived late or were delayed.
    /// </summary>
    private const int DefaultOverlapMinutes = 60;

    /// <summary>
    /// Creates a new SyncEngine instance.
    /// </summary>
    /// <param name="graphClient">The Graph API client for interacting with Microsoft 365.</param>
    /// <param name="database">The state database for tracking sync progress.</param>
    /// <param name="emlStorage">The EML storage service for saving messages.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <param name="transformationService">Optional transformation service for inline transformation during sync.</param>
    public SyncEngine(
        IGraphMailClient graphClient,
        IStateDatabase database,
        IEmlStorageService emlStorage,
        IAppLogger? logger = null,
        ITransformationService? transformationService = null)
    {
        _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _emlStorage = emlStorage ?? throw new ArgumentNullException(nameof(emlStorage));
        _logger = logger ?? LoggerFactory.CreateLogger<SyncEngine>();
        _transformationService = transformationService;
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
            _logger.Info("Starting sync (dryRun: {0}, parallel: {1}, checkpoint: {2})",
                options.DryRun, options.MaxParallelDownloads, options.CheckpointInterval);

            // Clean up any orphaned temp files from previous interrupted syncs
            _emlStorage.CleanupOrphanedTempFiles(TimeSpan.FromHours(1));

            // Phase 1: Get mailbox identifier
            var mailbox = options.Mailbox ?? await _graphClient.GetUserEmailAsync(cancellationToken);
            _logger.Info("Syncing mailbox: {0}", mailbox);

            // Phase 2: Get or create sync state
            var syncState = await GetOrCreateSyncStateAsync(mailbox, options.DryRun, cancellationToken);

            // Phase 3: Enumerate folders
            progressCallback?.Invoke(new SyncProgress
            {
                Phase = "Enumerating folders",
                TotalMessagesSynced = messagesSynced
            });

            var folders = await _graphClient.GetFoldersAsync(options.Mailbox, cancellationToken);
            var filteredFolders = FilterFolders(folders, options.ExcludeFolders);

            _logger.Info("Found {0} folders to sync (excluded {1})", filteredFolders.Count, folders.Count - filteredFolders.Count);

            // Phase 4: Store folder mappings
            if (!options.DryRun)
            {
                await StoreFolderMappingsAsync(filteredFolders, cancellationToken);
            }

            // Phase 5: Process each folder with streaming
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

                var (synced, skipped, folderErrors) = await ProcessFolderStreamingAsync(
                    folder,
                    mailbox,
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
        // Build set of folder IDs in our list to validate parent references
        var folderIds = new HashSet<string>(folders.Select(f => f.Id));

        // Sort folders so parents are inserted before children to satisfy foreign key constraints.
        var sortedFolders = TopologicalSortFolders(folders);

        foreach (var folder in sortedFolders)
        {
            // If the parent folder isn't in our list, set ParentFolderId to null
            var parentFolderId = folder.ParentFolderId;
            if (!string.IsNullOrEmpty(parentFolderId) && !folderIds.Contains(parentFolderId))
            {
                parentFolderId = null;
            }

            var folderEntity = new Folder
            {
                GraphId = folder.Id,
                ParentFolderId = parentFolderId,
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

    private static List<AppMailFolder> TopologicalSortFolders(List<AppMailFolder> folders)
    {
        var folderIds = new HashSet<string>(folders.Select(f => f.Id));
        var result = new List<AppMailFolder>(folders.Count);
        var processed = new HashSet<string>();
        var remaining = new Queue<AppMailFolder>(folders);
        var iterationCount = 0;
        var maxIterations = folders.Count * folders.Count;

        while (remaining.Count > 0 && iterationCount < maxIterations)
        {
            iterationCount++;
            var folder = remaining.Dequeue();

            var parentId = folder.ParentFolderId;
            var canProcess = string.IsNullOrEmpty(parentId) ||
                             !folderIds.Contains(parentId) ||
                             processed.Contains(parentId);

            if (canProcess)
            {
                result.Add(folder);
                processed.Add(folder.Id);
            }
            else
            {
                remaining.Enqueue(folder);
            }
        }

        while (remaining.Count > 0)
        {
            result.Add(remaining.Dequeue());
        }

        return result;
    }

    /// <summary>
    /// Processes a folder using streaming sync - downloads messages as each delta page arrives.
    /// </summary>
    private async Task<(int synced, int skipped, int errors)> ProcessFolderStreamingAsync(
        AppMailFolder folder,
        string mailbox,
        SyncOptions options,
        SyncProgressCallback? progressCallback,
        CancellationToken cancellationToken)
    {
        var synced = 0;
        var skipped = 0;
        var errors = 0;

        // Check for existing progress (resume support)
        var progress = await _database.GetFolderSyncProgressAsync(folder.Id, cancellationToken);
        var storedFolder = await _database.GetFolderAsync(folder.Id, cancellationToken);

        // Fallback: If folder not found by graph_id, try by local_path
        // This handles migration from mutable to immutable folder IDs
        if (storedFolder == null)
        {
            storedFolder = await _database.GetFolderByPathAsync(folder.FullPath, cancellationToken);
            if (storedFolder != null)
            {
                _logger.Debug("Folder {0}: found by path (ID changed from {1} to {2})",
                    folder.FullPath, storedFolder.GraphId, folder.Id);
            }
        }

        // Determine starting token: pending progress > stored delta token > null (initial)
        string? currentToken = progress?.PendingNextLink ?? storedFolder?.DeltaToken;

        _logger.Debug("Folder {0}: stored={1}, hasDeltaToken={2}, usingToken={3}",
            folder.FullPath,
            storedFolder != null,
            storedFolder?.DeltaToken != null,
            currentToken != null ? "yes (incremental)" : "no (full sync)");
        var startPageNumber = progress?.PendingPageNumber ?? 0;
        var startMessageIndex = progress?.PendingMessageIndex ?? 0;
        var storedLastSyncTime = storedFolder?.LastSyncTime;

        // Initialize progress tracking if not resuming
        if (progress == null && !options.DryRun)
        {
            progress = new FolderSyncProgress
            {
                FolderId = folder.Id,
                SyncStartedAt = DateTimeOffset.UtcNow,
                MessagesProcessed = 0
            };
            await _database.UpsertFolderSyncProgressAsync(progress, cancellationToken);
        }

        string? finalDeltaToken = null;
        var pageNumber = startPageNumber;
        var usedDateFallback = false;

        try
        {
            // Stream through delta pages, downloading messages as we go
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Fetch one page from delta query
                var result = await _graphClient.GetMessagesDeltaAsync(
                    folder.Id,
                    currentToken,
                    options.Mailbox,
                    cancellationToken);

                pageNumber++;

                _logger.Debug("Processing page {0} with {1} messages from folder {2}",
                    pageNumber, result.Items.Count, folder.FullPath);

                // Categorize messages in this page
                var newMessages = new List<MessageInfo>();
                var movedMessages = new List<MessageInfo>();
                var deletedMessages = new List<MessageInfo>();

                foreach (var message in result.Items)
                {
                    if (message.IsDeleted)
                    {
                        deletedMessages.Add(message);
                    }
                    else if (message.IsMoved)
                    {
                        movedMessages.Add(message);
                    }
                    else
                    {
                        newMessages.Add(message);
                    }
                }

                // Process deletions and moves immediately (these don't checkpoint individually)
                if (deletedMessages.Count > 0 && !options.DryRun)
                {
                    errors += await ProcessDeletedMessagesAsync(deletedMessages, options, cancellationToken);
                }

                if (movedMessages.Count > 0 && !options.DryRun)
                {
                    errors += await ProcessMovedMessagesAsync(movedMessages, options, cancellationToken);
                }

                // Process new messages with mini-batch checkpointing
                var (pageSynced, pageSkipped, pageErrors) = await ProcessPageMessagesWithCheckpointingAsync(
                    newMessages,
                    folder,
                    mailbox,
                    progress,
                    options,
                    progressCallback,
                    pageNumber,
                    startMessageIndex,
                    cancellationToken);

                synced += pageSynced;
                skipped += pageSkipped;
                errors += pageErrors;

                // Reset startMessageIndex after first page (only used for resume)
                startMessageIndex = 0;

                // Report progress
                progressCallback?.Invoke(new SyncProgress
                {
                    Phase = "Downloading messages",
                    CurrentFolder = folder.FullPath,
                    TotalMessagesInFolder = folder.TotalItemCount,
                    ProcessedMessagesInFolder = synced + skipped,
                    TotalMessagesSynced = synced,
                    CurrentPage = pageNumber,
                    MessagesInCurrentPage = result.Items.Count
                });

                // Check if more pages
                if (!result.HasMorePages)
                {
                    finalDeltaToken = result.DeltaToken;
                    break;
                }

                // Store checkpoint with nextLink for next page
                currentToken = result.NextPageLink;
                if (!options.DryRun && progress != null)
                {
                    progress.PendingNextLink = currentToken;
                    progress.PendingPageNumber = pageNumber;
                    progress.PendingMessageIndex = 0;
                    progress.LastCheckpointAt = DateTimeOffset.UtcNow;
                    await _database.UpsertFolderSyncProgressAsync(progress, cancellationToken);
                }
            }
        }
        catch (Exception ex) when (ShouldUseDateFallback(ex))
        {
            // Delta token expired or resync required - fall back to date-based query
            _logger.Warning("Delta query failed for folder {0}, falling back to date-based sync: {1}",
                folder.FullPath, ex.Message);

            usedDateFallback = true;

            if (storedLastSyncTime.HasValue)
            {
                var sinceDate = storedLastSyncTime.Value.AddMinutes(-DefaultOverlapMinutes);
                var dateMessages = await _graphClient.GetMessagesSinceDateAsync(
                    folder.Id,
                    sinceDate,
                    options.Mailbox,
                    cancellationToken);

                _logger.Debug("Date-based fallback returned {0} messages since {1}", dateMessages.Count, sinceDate);

                var (s, sk, e) = await ProcessPageMessagesWithCheckpointingAsync(
                    dateMessages.ToList(),
                    folder,
                    mailbox,
                    progress,
                    options,
                    progressCallback,
                    1,
                    0,
                    cancellationToken);

                synced += s;
                skipped += sk;
                errors += e;
            }
            else
            {
                // No previous sync time - need full resync without delta token
                // Recursively call with null token to start fresh
                _logger.Debug("No previous sync time for folder {0}, starting full sync", folder.FullPath);

                // Clear any existing progress and retry
                if (!options.DryRun)
                {
                    await _database.DeleteFolderSyncProgressAsync(folder.Id, cancellationToken);
                }

                return await ProcessFolderStreamingAsync(folder, mailbox, options, progressCallback, cancellationToken);
            }
        }

        // Folder complete - update delta token and clear progress
        if (!options.DryRun)
        {
            // Update folder's delta token
            var folderToUpdate = storedFolder ?? new Folder
            {
                GraphId = folder.Id,
                ParentFolderId = null,
                LocalPath = folder.FullPath,
                DisplayName = folder.DisplayName,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            if (!usedDateFallback && finalDeltaToken != null)
            {
                folderToUpdate.DeltaToken = finalDeltaToken;
            }

            folderToUpdate.LastSyncTime = DateTimeOffset.UtcNow;
            folderToUpdate.TotalItemCount = folder.TotalItemCount;
            folderToUpdate.UnreadItemCount = folder.UnreadItemCount;
            folderToUpdate.UpdatedAt = DateTimeOffset.UtcNow;

            await _database.UpsertFolderAsync(folderToUpdate, cancellationToken);

            // Clear progress (folder complete)
            await _database.DeleteFolderSyncProgressAsync(folder.Id, cancellationToken);
        }

        return (synced, skipped, errors);
    }

    /// <summary>
    /// Processes messages from a page with mini-batch checkpointing.
    /// </summary>
    private async Task<(int synced, int skipped, int errors)> ProcessPageMessagesWithCheckpointingAsync(
        List<MessageInfo> messages,
        AppMailFolder folder,
        string mailbox,
        FolderSyncProgress? progress,
        SyncOptions options,
        SyncProgressCallback? progressCallback,
        int pageNumber,
        int skipCount,
        CancellationToken cancellationToken)
    {
        var synced = 0;
        var skipped = 0;
        var errors = 0;

        // Skip already-processed messages (for resume)
        var messagesToProcess = messages.Skip(skipCount).ToList();

        if (messagesToProcess.Count == 0)
        {
            return (synced, skipped, errors);
        }

        // Process in mini-batches for checkpointing
        var checkpointInterval = Math.Max(1, options.CheckpointInterval);

        for (var i = 0; i < messagesToProcess.Count; i += checkpointInterval)
        {
            var miniBatch = messagesToProcess.Skip(i).Take(checkpointInterval).ToList();

            // Process mini-batch with parallelism
            using var semaphore = new SemaphoreSlim(options.MaxParallelDownloads);
            var tasks = miniBatch.Select(async message =>
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

            // Checkpoint after mini-batch
            if (!options.DryRun && progress != null)
            {
                progress.PendingMessageIndex = skipCount + i + miniBatch.Count;
                progress.MessagesProcessed += miniBatch.Count;
                progress.LastCheckpointAt = DateTimeOffset.UtcNow;
                await _database.UpsertFolderSyncProgressAsync(progress, cancellationToken);
            }

            // Report progress
            progressCallback?.Invoke(new SyncProgress
            {
                Phase = "Downloading messages",
                CurrentFolder = folder.FullPath,
                TotalMessagesInFolder = folder.TotalItemCount,
                ProcessedMessagesInFolder = synced + skipped,
                TotalMessagesSynced = synced,
                CurrentPage = pageNumber,
                MessagesInCurrentPage = messages.Count
            });
        }

        return (synced, skipped, errors);
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
                var immutableId = messageInfo.ImmutableId ?? messageInfo.Id;
                var existingMessage = await _database.GetMessageByImmutableIdAsync(immutableId, cancellationToken);

                if (existingMessage == null)
                {
                    _logger.Debug("Moved message {0} not found in database, skipping", immutableId);
                    continue;
                }

                if (messageInfo.NewParentFolderId == null)
                {
                    _logger.Warning("Moved message {0} has no new parent folder ID", immutableId);
                    continue;
                }

                var newFolder = await _database.GetFolderAsync(messageInfo.NewParentFolderId, cancellationToken);
                if (newFolder == null)
                {
                    _logger.Debug("New folder {0} not found in database for moved message {1}", messageInfo.NewParentFolderId, immutableId);
                    continue;
                }

                var newFolderPath = newFolder.LocalPath;
                var oldFolderPath = existingMessage.FolderPath;

                if (string.Equals(newFolderPath, oldFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (options.DryRun)
                {
                    _logger.Debug("Would move message {0} from {1} to {2}",
                        existingMessage.Subject ?? existingMessage.ImmutableId,
                        oldFolderPath,
                        newFolderPath);
                    continue;
                }

                var newLocalPath = await _emlStorage.MoveEmlAsync(
                    existingMessage.LocalPath,
                    newFolderPath,
                    cancellationToken);

                existingMessage.LocalPath = newLocalPath;
                existingMessage.FolderPath = newFolderPath;
                existingMessage.UpdatedAt = DateTimeOffset.UtcNow;

                await _database.UpdateMessageAsync(existingMessage, cancellationToken);

                _logger.Debug("Moved message {0} from {1} to {2}",
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
                var immutableId = messageInfo.ImmutableId ?? messageInfo.Id;
                var existingMessage = await _database.GetMessageByImmutableIdAsync(immutableId, cancellationToken);

                if (existingMessage == null)
                {
                    existingMessage = await _database.GetMessageAsync(messageInfo.Id, cancellationToken);
                }

                if (existingMessage == null)
                {
                    _logger.Debug("Deleted message {0} not found in database, skipping", immutableId);
                    continue;
                }

                if (existingMessage.QuarantinedAt != null)
                {
                    _logger.Debug("Message {0} is already quarantined, skipping", immutableId);
                    continue;
                }

                if (options.DryRun)
                {
                    _logger.Debug("Would quarantine message {0}: {1}",
                        existingMessage.Subject ?? existingMessage.ImmutableId,
                        existingMessage.LocalPath);
                    continue;
                }

                string newLocalPath;
                try
                {
                    newLocalPath = await _emlStorage.MoveToQuarantineAsync(
                        existingMessage.LocalPath,
                        cancellationToken);
                }
                catch (FileNotFoundException)
                {
                    _logger.Warning("EML file not found for deleted message {0}, updating database only", immutableId);
                    newLocalPath = existingMessage.LocalPath;
                }

                existingMessage.LocalPath = newLocalPath;
                existingMessage.QuarantinedAt = DateTimeOffset.UtcNow;
                existingMessage.QuarantineReason = "deleted_in_m365";
                existingMessage.UpdatedAt = DateTimeOffset.UtcNow;

                await _database.UpdateMessageAsync(existingMessage, cancellationToken);

                _logger.Debug("Quarantined deleted message {0}: {1}",
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

    private static bool ShouldUseDateFallback(Exception ex)
    {
        var message = ex.Message.ToUpperInvariant();
        return message.Contains("RESYNC") ||
               message.Contains("DELTA") ||
               message.Contains("SYNC_STATE") ||
               message.Contains("TOKEN") && (message.Contains("INVALID") || message.Contains("EXPIRED"));
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

            using var mimeStream = await _graphClient.DownloadMessageMimeAsync(
                messageInfo.Id,
                options.Mailbox,
                cancellationToken);

            var localPath = await _emlStorage.StoreEmlAsync(
                mimeStream,
                folder.FullPath,
                messageInfo.Subject,
                messageInfo.ReceivedDateTime,
                cancellationToken);

            var fileSize = _emlStorage.GetFileSize(localPath);

            var message = new Message
            {
                GraphId = messageInfo.Id,
                ImmutableId = immutableId,
                LocalPath = localPath,
                FolderPath = folder.FullPath,
                Subject = messageInfo.Subject,
                Sender = messageInfo.From,
                Recipients = null,
                ReceivedTime = messageInfo.ReceivedDateTime,
                Size = fileSize,
                HasAttachments = messageInfo.HasAttachments,
                InReplyTo = null,
                ConversationId = null,
                QuarantinedAt = null,
                QuarantineReason = null,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await _database.InsertMessageAsync(message, cancellationToken);

            _logger.Debug("Synced message: {0}", messageInfo.Subject ?? messageInfo.Id);

            // Perform inline transformation if enabled
            if (_transformationService != null && ShouldTransform(options))
            {
                var inlineOptions = new InlineTransformOptions
                {
                    GenerateHtml = options.GenerateHtml,
                    GenerateMarkdown = options.GenerateMarkdown,
                    ExtractAttachments = options.ExtractAttachments,
                    HtmlOptions = options.HtmlOptions,
                    AttachmentOptions = options.AttachmentOptions
                };

                var transformSuccess = await _transformationService.TransformSingleMessageAsync(
                    message,
                    inlineOptions,
                    cancellationToken);

                if (!transformSuccess)
                {
                    _logger.Warning("Inline transformation failed for message {0}", messageInfo.Subject ?? messageInfo.Id);
                    // Note: We don't fail the sync if transformation fails - the EML is still stored
                }
            }

            return MessageProcessResult.Synced;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing message {0}: {1}", messageInfo.Id, ex.Message);
            return MessageProcessResult.Error;
        }
    }

    private static bool ShouldTransform(SyncOptions options) =>
        options.GenerateHtml || options.GenerateMarkdown || options.ExtractAttachments;

    private enum MessageProcessResult
    {
        Synced,
        Skipped,
        Error
    }
}
