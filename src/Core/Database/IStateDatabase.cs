using M365MailMirror.Core.Database.Entities;

namespace M365MailMirror.Core.Database;

/// <summary>
/// Interface for the state database that tracks sync state, messages, transformations, and folders.
/// The database stores metadata only - message content is stored in EML files.
/// </summary>
public interface IStateDatabase : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the current schema version of the database.
    /// </summary>
    /// <returns>The schema version number, or 0 if no schema is initialized.</returns>
    Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes the database schema if not already present.
    /// Runs all necessary migrations to bring the schema to the current version.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a new database transaction for batch operations.
    /// </summary>
    /// <returns>A transaction object that must be committed or disposed.</returns>
    Task<IDatabaseTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    // Sync State Operations

    /// <summary>
    /// Gets the sync state for a mailbox.
    /// </summary>
    /// <param name="mailbox">The mailbox identifier.</param>
    /// <returns>The sync state, or null if not found.</returns>
    Task<SyncState?> GetSyncStateAsync(string mailbox, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates the sync state for a mailbox.
    /// </summary>
    Task UpsertSyncStateAsync(SyncState syncState, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all sync states.
    /// </summary>
    Task<IReadOnlyList<SyncState>> GetAllSyncStatesAsync(CancellationToken cancellationToken = default);

    // Message Operations

    /// <summary>
    /// Gets a message by its Graph ID.
    /// </summary>
    Task<Message?> GetMessageAsync(string graphId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a message by its immutable ID.
    /// </summary>
    Task<Message?> GetMessageByImmutableIdAsync(string immutableId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new message record.
    /// </summary>
    Task InsertMessageAsync(Message message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing message record.
    /// </summary>
    Task UpdateMessageAsync(Message message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a message record by its Graph ID.
    /// </summary>
    Task DeleteMessageAsync(string graphId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all messages in a folder.
    /// </summary>
    Task<IReadOnlyList<Message>> GetMessagesByFolderAsync(string folderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all quarantined messages.
    /// </summary>
    Task<IReadOnlyList<Message>> GetQuarantinedMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total message count.
    /// </summary>
    Task<long> GetMessageCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the message count for a specific folder.
    /// </summary>
    Task<long> GetMessageCountByFolderAsync(string folderPath, CancellationToken cancellationToken = default);

    // Folder Operations

    /// <summary>
    /// Gets a folder by its Graph ID.
    /// </summary>
    Task<Folder?> GetFolderAsync(string graphId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a folder by its local path.
    /// </summary>
    Task<Folder?> GetFolderByPathAsync(string localPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all folders.
    /// </summary>
    Task<IReadOnlyList<Folder>> GetAllFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates a folder record.
    /// </summary>
    Task UpsertFolderAsync(Folder folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a folder record.
    /// </summary>
    Task DeleteFolderAsync(string graphId, CancellationToken cancellationToken = default);

    // Transformation Operations

    /// <summary>
    /// Gets a transformation record for a message and type.
    /// </summary>
    Task<Transformation?> GetTransformationAsync(string messageId, string transformationType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all transformations for a message.
    /// </summary>
    Task<IReadOnlyList<Transformation>> GetTransformationsForMessageAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates a transformation record.
    /// </summary>
    Task UpsertTransformationAsync(Transformation transformation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all transformations for a message.
    /// </summary>
    Task DeleteTransformationsForMessageAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of transformations by type.
    /// </summary>
    Task<long> GetTransformationCountByTypeAsync(string transformationType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets messages needing a specific transformation type.
    /// Returns messages where transformation is missing, config version changed, or file is missing.
    /// </summary>
    Task<IReadOnlyList<Message>> GetMessagesNeedingTransformationAsync(
        string transformationType,
        string currentConfigVersion,
        CancellationToken cancellationToken = default);

    // Attachment Operations

    /// <summary>
    /// Gets all attachments for a message.
    /// </summary>
    Task<IReadOnlyList<Attachment>> GetAttachmentsForMessageAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts an attachment record.
    /// </summary>
    Task<long> InsertAttachmentAsync(Attachment attachment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all attachments for a message.
    /// </summary>
    Task DeleteAttachmentsForMessageAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all skipped attachments.
    /// </summary>
    Task<IReadOnlyList<Attachment>> GetSkippedAttachmentsAsync(CancellationToken cancellationToken = default);

    // ZIP Extraction Operations

    /// <summary>
    /// Gets a ZIP extraction record by attachment ID.
    /// </summary>
    Task<ZipExtraction?> GetZipExtractionAsync(long attachmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a ZIP extraction record.
    /// </summary>
    Task<long> InsertZipExtractionAsync(ZipExtraction zipExtraction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all ZIP extractions for a message.
    /// </summary>
    Task DeleteZipExtractionsForMessageAsync(string messageId, CancellationToken cancellationToken = default);

    // ZIP Extracted File Operations

    /// <summary>
    /// Gets all files extracted from a ZIP.
    /// </summary>
    Task<IReadOnlyList<ZipExtractedFile>> GetZipExtractedFilesAsync(long zipExtractionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a ZIP extracted file record.
    /// </summary>
    Task InsertZipExtractedFileAsync(ZipExtractedFile file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch inserts multiple ZIP extracted file records.
    /// </summary>
    Task InsertZipExtractedFilesAsync(IEnumerable<ZipExtractedFile> files, CancellationToken cancellationToken = default);
}
