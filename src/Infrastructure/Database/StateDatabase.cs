using System.Globalization;
using M365MailMirror.Core.Database;
using M365MailMirror.Core.Database.Entities;
using M365MailMirror.Core.Logging;
using Microsoft.Data.Sqlite;

namespace M365MailMirror.Infrastructure.Database;

/// <summary>
/// SQLite implementation of the state database.
/// Stores metadata for sync state, messages, transformations, folders, and attachments.
/// </summary>
public class StateDatabase : IStateDatabase
{
    private readonly string _connectionString;
    private readonly IAppLogger _logger;
    private SqliteConnection? _connection;
    private bool _disposed;

    /// <summary>
    /// The current schema version. Increment when making schema changes.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// The default database filename.
    /// </summary>
    public const string DefaultDatabaseFilename = ".sync.db";

    /// <summary>
    /// The subdirectory where the database is stored within the archive root.
    /// </summary>
    public const string DatabaseDirectory = "status";

    /// <summary>
    /// Creates a new StateDatabase instance.
    /// </summary>
    /// <param name="databasePath">Full path to the database file.</param>
    /// <param name="logger">Optional logger instance.</param>
    public StateDatabase(string databasePath, IAppLogger? logger = null)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

        _logger = logger ?? LoggerFactory.CreateLogger<StateDatabase>();
    }

    /// <summary>
    /// Creates a StateDatabase with an in-memory database for testing.
    /// </summary>
    public static StateDatabase CreateInMemory(IAppLogger? logger = null)
    {
        return new StateDatabase(":memory:", logger);
    }

    private async Task<SqliteConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection == null)
        {
            _connection = new SqliteConnection(_connectionString);
            await _connection.OpenAsync(cancellationToken);

            // Enable WAL mode for better concurrency
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        return _connection;
    }

    /// <inheritdoc />
    public async Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        // Check if schema_version table exists
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = @"
            SELECT name FROM sqlite_master
            WHERE type='table' AND name='schema_version'";

        var exists = await checkCmd.ExecuteScalarAsync(cancellationToken);
        if (exists == null)
        {
            return 0;
        }

        using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "SELECT MAX(version) FROM schema_version";
        var result = await versionCmd.ExecuteScalarAsync(cancellationToken);

        return result == DBNull.Value || result == null ? 0 : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = await GetSchemaVersionAsync(cancellationToken);

        if (currentVersion < CurrentSchemaVersion)
        {
            _logger.Info("Initializing database schema from version {0} to {1}", currentVersion, CurrentSchemaVersion);
            await MigrateSchemaAsync(currentVersion, cancellationToken);
        }
    }

    private async Task MigrateSchemaAsync(int fromVersion, CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var transaction = connection.BeginTransaction();
        try
        {
            if (fromVersion < 1)
            {
                await ApplySchemaV1Async(connection, cancellationToken);
            }

            if (fromVersion < 2)
            {
                await ApplySchemaV2Async(connection, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            _logger.Info("Database schema migration completed successfully");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.Error(ex, "Database schema migration failed");
            throw;
        }
    }

    private static async Task ApplySchemaV1Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = SchemaV1;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        // Record schema version
        using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "INSERT INTO schema_version (version) VALUES (1)";
        await versionCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string SchemaV1 = @"
-- Schema version tracking
CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY
);

-- Sync state per mailbox
CREATE TABLE IF NOT EXISTS sync_state (
    mailbox TEXT PRIMARY KEY,
    last_sync_time TEXT NOT NULL,
    last_batch_id INTEGER NOT NULL DEFAULT 0,
    last_delta_token TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

-- Message tracking
CREATE TABLE IF NOT EXISTS messages (
    graph_id TEXT PRIMARY KEY,
    immutable_id TEXT NOT NULL UNIQUE,
    local_path TEXT NOT NULL,
    folder_path TEXT NOT NULL,
    subject TEXT,
    sender TEXT,
    recipients TEXT,
    received_time TEXT NOT NULL,
    size INTEGER NOT NULL,
    has_attachments INTEGER NOT NULL,
    in_reply_to TEXT,
    conversation_id TEXT,
    quarantined_at TEXT,
    quarantine_reason TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

-- Transformation state
CREATE TABLE IF NOT EXISTS transformations (
    message_id TEXT NOT NULL,
    transformation_type TEXT NOT NULL,
    applied_at TEXT NOT NULL,
    config_version TEXT NOT NULL,
    output_path TEXT NOT NULL,
    PRIMARY KEY (message_id, transformation_type),
    FOREIGN KEY (message_id) REFERENCES messages(graph_id) ON DELETE CASCADE
);

-- Attachment extraction tracking
CREATE TABLE IF NOT EXISTS attachments (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    message_id TEXT NOT NULL,
    filename TEXT NOT NULL,
    file_path TEXT NOT NULL,
    size_bytes INTEGER NOT NULL,
    content_type TEXT,
    is_inline INTEGER NOT NULL,
    skipped INTEGER NOT NULL DEFAULT 0,
    skip_reason TEXT,
    extracted_at TEXT NOT NULL,
    FOREIGN KEY (message_id) REFERENCES messages(graph_id) ON DELETE CASCADE
);

-- ZIP extraction tracking
CREATE TABLE IF NOT EXISTS zip_extractions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    attachment_id INTEGER NOT NULL,
    message_id TEXT NOT NULL,
    zip_filename TEXT NOT NULL,
    extraction_path TEXT NOT NULL,
    extracted INTEGER NOT NULL,
    skip_reason TEXT,
    file_count INTEGER,
    total_size_bytes INTEGER,
    has_executables INTEGER,
    has_unsafe_paths INTEGER,
    is_encrypted INTEGER,
    extracted_at TEXT NOT NULL,
    FOREIGN KEY (attachment_id) REFERENCES attachments(id) ON DELETE CASCADE,
    FOREIGN KEY (message_id) REFERENCES messages(graph_id) ON DELETE CASCADE
);

-- ZIP extracted file tracking (individual files within ZIPs)
CREATE TABLE IF NOT EXISTS zip_extracted_files (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    zip_extraction_id INTEGER NOT NULL,
    relative_path TEXT NOT NULL,
    extracted_path TEXT NOT NULL,
    size_bytes INTEGER NOT NULL,
    FOREIGN KEY (zip_extraction_id) REFERENCES zip_extractions(id) ON DELETE CASCADE
);

-- Folder mapping
CREATE TABLE IF NOT EXISTS folders (
    graph_id TEXT PRIMARY KEY,
    parent_folder_id TEXT,
    local_path TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    total_item_count INTEGER,
    unread_item_count INTEGER,
    delta_token TEXT,
    last_sync_time TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    FOREIGN KEY (parent_folder_id) REFERENCES folders(graph_id)
);

-- Indexes for common queries
CREATE INDEX IF NOT EXISTS idx_messages_folder ON messages(folder_path);
CREATE INDEX IF NOT EXISTS idx_messages_received ON messages(received_time);
CREATE INDEX IF NOT EXISTS idx_messages_conversation ON messages(conversation_id);
CREATE INDEX IF NOT EXISTS idx_messages_quarantined ON messages(quarantined_at) WHERE quarantined_at IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_transformations_type ON transformations(transformation_type);
CREATE INDEX IF NOT EXISTS idx_transformations_config ON transformations(config_version);
CREATE INDEX IF NOT EXISTS idx_attachments_message ON attachments(message_id);
CREATE INDEX IF NOT EXISTS idx_attachments_skipped ON attachments(skipped) WHERE skipped = 1;
CREATE INDEX IF NOT EXISTS idx_zip_extractions_message ON zip_extractions(message_id);
CREATE INDEX IF NOT EXISTS idx_zip_extractions_attachment ON zip_extractions(attachment_id);
CREATE INDEX IF NOT EXISTS idx_zip_extracted_files_zip ON zip_extracted_files(zip_extraction_id);
";

    private const string SchemaV2 = @"
-- Folder sync progress tracking for streaming sync
-- Created when sync starts on a folder, deleted when complete
-- Enables fine-grained resumption from exact page and message position
CREATE TABLE IF NOT EXISTS folder_sync_progress (
    folder_id TEXT PRIMARY KEY,
    pending_next_link TEXT,
    pending_page_number INTEGER NOT NULL DEFAULT 0,
    pending_message_index INTEGER NOT NULL DEFAULT 0,
    sync_started_at TEXT,
    last_checkpoint_at TEXT,
    messages_processed INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (folder_id) REFERENCES folders(graph_id) ON DELETE CASCADE
);
";

    private static async Task ApplySchemaV2Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = SchemaV2;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        // Record schema version
        using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "INSERT INTO schema_version (version) VALUES (2)";
        await versionCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IDatabaseTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        var transaction = connection.BeginTransaction();
        return new SqliteDatabaseTransaction(transaction);
    }

    #region Sync State Operations

    /// <inheritdoc />
    public async Task<SyncState?> GetSyncStateAsync(string mailbox, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT mailbox, last_sync_time, last_batch_id, last_delta_token, created_at, updated_at
            FROM sync_state
            WHERE mailbox = @mailbox";
        cmd.Parameters.AddWithValue("@mailbox", mailbox);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new SyncState
            {
                Mailbox = reader.GetString(0),
                LastSyncTime = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                LastBatchId = reader.GetInt32(2),
                LastDeltaToken = reader.IsDBNull(3) ? null : reader.GetString(3),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture)
            };
        }

        return null;
    }

    /// <inheritdoc />
    public async Task UpsertSyncStateAsync(SyncState syncState, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sync_state (mailbox, last_sync_time, last_batch_id, last_delta_token, created_at, updated_at)
            VALUES (@mailbox, @lastSyncTime, @lastBatchId, @lastDeltaToken, @createdAt, @updatedAt)
            ON CONFLICT(mailbox) DO UPDATE SET
                last_sync_time = @lastSyncTime,
                last_batch_id = @lastBatchId,
                last_delta_token = @lastDeltaToken,
                updated_at = @updatedAt";

        cmd.Parameters.AddWithValue("@mailbox", syncState.Mailbox);
        cmd.Parameters.AddWithValue("@lastSyncTime", syncState.LastSyncTime.ToString("O"));
        cmd.Parameters.AddWithValue("@lastBatchId", syncState.LastBatchId);
        cmd.Parameters.AddWithValue("@lastDeltaToken", (object?)syncState.LastDeltaToken ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", syncState.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updatedAt", syncState.UpdatedAt.ToString("O"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SyncState>> GetAllSyncStatesAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT mailbox, last_sync_time, last_batch_id, last_delta_token, created_at, updated_at
            FROM sync_state
            ORDER BY mailbox";

        var results = new List<SyncState>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SyncState
            {
                Mailbox = reader.GetString(0),
                LastSyncTime = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                LastBatchId = reader.GetInt32(2),
                LastDeltaToken = reader.IsDBNull(3) ? null : reader.GetString(3),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture)
            });
        }

        return results;
    }

    #endregion

    #region Message Operations

    /// <inheritdoc />
    public async Task<Message?> GetMessageAsync(string graphId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT graph_id, immutable_id, local_path, folder_path, subject, sender, recipients,
                   received_time, size, has_attachments, in_reply_to, conversation_id,
                   quarantined_at, quarantine_reason, created_at, updated_at
            FROM messages
            WHERE graph_id = @graphId";
        cmd.Parameters.AddWithValue("@graphId", graphId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadMessage(reader);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<Message?> GetMessageByImmutableIdAsync(string immutableId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT graph_id, immutable_id, local_path, folder_path, subject, sender, recipients,
                   received_time, size, has_attachments, in_reply_to, conversation_id,
                   quarantined_at, quarantine_reason, created_at, updated_at
            FROM messages
            WHERE immutable_id = @immutableId";
        cmd.Parameters.AddWithValue("@immutableId", immutableId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadMessage(reader);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task InsertMessageAsync(Message message, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO messages (graph_id, immutable_id, local_path, folder_path, subject, sender, recipients,
                                  received_time, size, has_attachments, in_reply_to, conversation_id,
                                  quarantined_at, quarantine_reason, created_at, updated_at)
            VALUES (@graphId, @immutableId, @localPath, @folderPath, @subject, @sender, @recipients,
                    @receivedTime, @size, @hasAttachments, @inReplyTo, @conversationId,
                    @quarantinedAt, @quarantineReason, @createdAt, @updatedAt)";

        AddMessageParameters(cmd, message);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateMessageAsync(Message message, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE messages SET
                immutable_id = @immutableId,
                local_path = @localPath,
                folder_path = @folderPath,
                subject = @subject,
                sender = @sender,
                recipients = @recipients,
                received_time = @receivedTime,
                size = @size,
                has_attachments = @hasAttachments,
                in_reply_to = @inReplyTo,
                conversation_id = @conversationId,
                quarantined_at = @quarantinedAt,
                quarantine_reason = @quarantineReason,
                updated_at = @updatedAt
            WHERE graph_id = @graphId";

        AddMessageParameters(cmd, message);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteMessageAsync(string graphId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM messages WHERE graph_id = @graphId";
        cmd.Parameters.AddWithValue("@graphId", graphId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Message>> GetMessagesByFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT graph_id, immutable_id, local_path, folder_path, subject, sender, recipients,
                   received_time, size, has_attachments, in_reply_to, conversation_id,
                   quarantined_at, quarantine_reason, created_at, updated_at
            FROM messages
            WHERE folder_path = @folderPath AND quarantined_at IS NULL
            ORDER BY received_time DESC";
        cmd.Parameters.AddWithValue("@folderPath", folderPath);

        var messages = new List<Message>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(ReadMessage(reader));
        }

        return messages;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Message>> GetQuarantinedMessagesAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT graph_id, immutable_id, local_path, folder_path, subject, sender, recipients,
                   received_time, size, has_attachments, in_reply_to, conversation_id,
                   quarantined_at, quarantine_reason, created_at, updated_at
            FROM messages
            WHERE quarantined_at IS NOT NULL
            ORDER BY quarantined_at DESC";

        var messages = new List<Message>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(ReadMessage(reader));
        }

        return messages;
    }

    /// <inheritdoc />
    public async Task<long> GetMessageCountAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM messages WHERE quarantined_at IS NULL";

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<long> GetMessageCountByFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM messages WHERE folder_path = @folderPath AND quarantined_at IS NULL";
        cmd.Parameters.AddWithValue("@folderPath", folderPath);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static Message ReadMessage(SqliteDataReader reader)
    {
        return new Message
        {
            GraphId = reader.GetString(0),
            ImmutableId = reader.GetString(1),
            LocalPath = reader.GetString(2),
            FolderPath = reader.GetString(3),
            Subject = reader.IsDBNull(4) ? null : reader.GetString(4),
            Sender = reader.IsDBNull(5) ? null : reader.GetString(5),
            Recipients = reader.IsDBNull(6) ? null : reader.GetString(6),
            ReceivedTime = DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
            Size = reader.GetInt64(8),
            HasAttachments = reader.GetInt32(9) != 0,
            InReplyTo = reader.IsDBNull(10) ? null : reader.GetString(10),
            ConversationId = reader.IsDBNull(11) ? null : reader.GetString(11),
            QuarantinedAt = reader.IsDBNull(12) ? null : DateTimeOffset.Parse(reader.GetString(12), CultureInfo.InvariantCulture),
            QuarantineReason = reader.IsDBNull(13) ? null : reader.GetString(13),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(14), CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(15), CultureInfo.InvariantCulture)
        };
    }

    private static void AddMessageParameters(SqliteCommand cmd, Message message)
    {
        cmd.Parameters.AddWithValue("@graphId", message.GraphId);
        cmd.Parameters.AddWithValue("@immutableId", message.ImmutableId);
        cmd.Parameters.AddWithValue("@localPath", message.LocalPath);
        cmd.Parameters.AddWithValue("@folderPath", message.FolderPath);
        cmd.Parameters.AddWithValue("@subject", (object?)message.Subject ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sender", (object?)message.Sender ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@recipients", (object?)message.Recipients ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@receivedTime", message.ReceivedTime.ToString("O"));
        cmd.Parameters.AddWithValue("@size", message.Size);
        cmd.Parameters.AddWithValue("@hasAttachments", message.HasAttachments ? 1 : 0);
        cmd.Parameters.AddWithValue("@inReplyTo", (object?)message.InReplyTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@conversationId", (object?)message.ConversationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@quarantinedAt", message.QuarantinedAt.HasValue ? message.QuarantinedAt.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("@quarantineReason", (object?)message.QuarantineReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", message.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updatedAt", message.UpdatedAt.ToString("O"));
    }

    #endregion

    #region Folder Operations

    /// <inheritdoc />
    public async Task<Folder?> GetFolderAsync(string graphId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT graph_id, parent_folder_id, local_path, display_name,
                   total_item_count, unread_item_count, delta_token, last_sync_time,
                   created_at, updated_at
            FROM folders
            WHERE graph_id = @graphId";
        cmd.Parameters.AddWithValue("@graphId", graphId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadFolder(reader);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<Folder?> GetFolderByPathAsync(string localPath, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT graph_id, parent_folder_id, local_path, display_name,
                   total_item_count, unread_item_count, delta_token, last_sync_time,
                   created_at, updated_at
            FROM folders
            WHERE local_path = @localPath";
        cmd.Parameters.AddWithValue("@localPath", localPath);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadFolder(reader);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Folder>> GetAllFoldersAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT graph_id, parent_folder_id, local_path, display_name,
                   total_item_count, unread_item_count, delta_token, last_sync_time,
                   created_at, updated_at
            FROM folders
            ORDER BY local_path";

        var folders = new List<Folder>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            folders.Add(ReadFolder(reader));
        }

        return folders;
    }

    /// <inheritdoc />
    public async Task UpsertFolderAsync(Folder folder, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        // Handle migration from mutable to immutable folder IDs:
        // If a folder exists with the same local_path but different graph_id,
        // copy its delta_token and last_sync_time before deleting it
        using (var selectCmd = connection.CreateCommand())
        {
            selectCmd.CommandText = @"
                SELECT delta_token, last_sync_time FROM folders
                WHERE local_path = @localPath AND graph_id != @graphId";
            selectCmd.Parameters.AddWithValue("@localPath", folder.LocalPath);
            selectCmd.Parameters.AddWithValue("@graphId", folder.GraphId);

            using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                // Copy delta_token and last_sync_time from old record if not already set
                folder.DeltaToken ??= reader.IsDBNull(0) ? null : reader.GetString(0);
                folder.LastSyncTime ??= reader.IsDBNull(1) ? null : DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
            }
        }

        // Delete the old record with different graph_id
        using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.CommandText = "DELETE FROM folders WHERE local_path = @localPath AND graph_id != @graphId";
            deleteCmd.Parameters.AddWithValue("@localPath", folder.LocalPath);
            deleteCmd.Parameters.AddWithValue("@graphId", folder.GraphId);
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO folders (graph_id, parent_folder_id, local_path, display_name,
                                 total_item_count, unread_item_count, delta_token, last_sync_time,
                                 created_at, updated_at)
            VALUES (@graphId, @parentFolderId, @localPath, @displayName,
                    @totalItemCount, @unreadItemCount, @deltaToken, @lastSyncTime,
                    @createdAt, @updatedAt)
            ON CONFLICT(graph_id) DO UPDATE SET
                parent_folder_id = @parentFolderId,
                local_path = @localPath,
                display_name = @displayName,
                total_item_count = @totalItemCount,
                unread_item_count = @unreadItemCount,
                delta_token = COALESCE(@deltaToken, folders.delta_token),
                last_sync_time = COALESCE(@lastSyncTime, folders.last_sync_time),
                updated_at = @updatedAt";

        cmd.Parameters.AddWithValue("@graphId", folder.GraphId);
        cmd.Parameters.AddWithValue("@parentFolderId", (object?)folder.ParentFolderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@localPath", folder.LocalPath);
        cmd.Parameters.AddWithValue("@displayName", folder.DisplayName);
        cmd.Parameters.AddWithValue("@totalItemCount", folder.TotalItemCount.HasValue ? folder.TotalItemCount.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@unreadItemCount", folder.UnreadItemCount.HasValue ? folder.UnreadItemCount.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@deltaToken", (object?)folder.DeltaToken ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lastSyncTime", folder.LastSyncTime.HasValue ? folder.LastSyncTime.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", folder.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updatedAt", folder.UpdatedAt.ToString("O"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteFolderAsync(string graphId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM folders WHERE graph_id = @graphId";
        cmd.Parameters.AddWithValue("@graphId", graphId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Folder ReadFolder(SqliteDataReader reader)
    {
        return new Folder
        {
            GraphId = reader.GetString(0),
            ParentFolderId = reader.IsDBNull(1) ? null : reader.GetString(1),
            LocalPath = reader.GetString(2),
            DisplayName = reader.GetString(3),
            TotalItemCount = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            UnreadItemCount = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            DeltaToken = reader.IsDBNull(6) ? null : reader.GetString(6),
            LastSyncTime = reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture)
        };
    }

    #endregion

    #region Transformation Operations

    /// <inheritdoc />
    public async Task<Transformation?> GetTransformationAsync(string messageId, string transformationType, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT message_id, transformation_type, applied_at, config_version, output_path
            FROM transformations
            WHERE message_id = @messageId AND transformation_type = @transformationType";
        cmd.Parameters.AddWithValue("@messageId", messageId);
        cmd.Parameters.AddWithValue("@transformationType", transformationType);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadTransformation(reader);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Transformation>> GetTransformationsForMessageAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT message_id, transformation_type, applied_at, config_version, output_path
            FROM transformations
            WHERE message_id = @messageId";
        cmd.Parameters.AddWithValue("@messageId", messageId);

        var transformations = new List<Transformation>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            transformations.Add(ReadTransformation(reader));
        }

        return transformations;
    }

    /// <inheritdoc />
    public async Task UpsertTransformationAsync(Transformation transformation, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO transformations (message_id, transformation_type, applied_at, config_version, output_path)
            VALUES (@messageId, @transformationType, @appliedAt, @configVersion, @outputPath)
            ON CONFLICT(message_id, transformation_type) DO UPDATE SET
                applied_at = @appliedAt,
                config_version = @configVersion,
                output_path = @outputPath";

        cmd.Parameters.AddWithValue("@messageId", transformation.MessageId);
        cmd.Parameters.AddWithValue("@transformationType", transformation.TransformationType);
        cmd.Parameters.AddWithValue("@appliedAt", transformation.AppliedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@configVersion", transformation.ConfigVersion);
        cmd.Parameters.AddWithValue("@outputPath", transformation.OutputPath);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteTransformationsForMessageAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM transformations WHERE message_id = @messageId";
        cmd.Parameters.AddWithValue("@messageId", messageId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> GetTransformationCountByTypeAsync(string transformationType, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM transformations WHERE transformation_type = @transformationType";
        cmd.Parameters.AddWithValue("@transformationType", transformationType);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Message>> GetMessagesNeedingTransformationAsync(
        string transformationType,
        string currentConfigVersion,
        CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT m.graph_id, m.immutable_id, m.local_path, m.folder_path, m.subject, m.sender, m.recipients,
                   m.received_time, m.size, m.has_attachments, m.in_reply_to, m.conversation_id,
                   m.quarantined_at, m.quarantine_reason, m.created_at, m.updated_at
            FROM messages m
            LEFT JOIN transformations t ON m.graph_id = t.message_id AND t.transformation_type = @transformationType
            WHERE m.quarantined_at IS NULL
              AND (t.message_id IS NULL OR t.config_version != @configVersion)
            ORDER BY m.received_time DESC";

        cmd.Parameters.AddWithValue("@transformationType", transformationType);
        cmd.Parameters.AddWithValue("@configVersion", currentConfigVersion);

        var messages = new List<Message>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(ReadMessage(reader));
        }

        return messages;
    }

    private static Transformation ReadTransformation(SqliteDataReader reader)
    {
        return new Transformation
        {
            MessageId = reader.GetString(0),
            TransformationType = reader.GetString(1),
            AppliedAt = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
            ConfigVersion = reader.GetString(3),
            OutputPath = reader.GetString(4)
        };
    }

    #endregion

    #region Attachment Operations

    /// <inheritdoc />
    public async Task<IReadOnlyList<Attachment>> GetAttachmentsForMessageAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, message_id, filename, file_path, size_bytes, content_type,
                   is_inline, skipped, skip_reason, extracted_at
            FROM attachments
            WHERE message_id = @messageId
            ORDER BY id";
        cmd.Parameters.AddWithValue("@messageId", messageId);

        var attachments = new List<Attachment>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            attachments.Add(ReadAttachment(reader));
        }

        return attachments;
    }

    /// <inheritdoc />
    public async Task<long> InsertAttachmentAsync(Attachment attachment, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO attachments (message_id, filename, file_path, size_bytes, content_type,
                                     is_inline, skipped, skip_reason, extracted_at)
            VALUES (@messageId, @filename, @filePath, @sizeBytes, @contentType,
                    @isInline, @skipped, @skipReason, @extractedAt);
            SELECT last_insert_rowid();";

        cmd.Parameters.AddWithValue("@messageId", attachment.MessageId);
        cmd.Parameters.AddWithValue("@filename", attachment.Filename);
        cmd.Parameters.AddWithValue("@filePath", attachment.FilePath);
        cmd.Parameters.AddWithValue("@sizeBytes", attachment.SizeBytes);
        cmd.Parameters.AddWithValue("@contentType", (object?)attachment.ContentType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@isInline", attachment.IsInline ? 1 : 0);
        cmd.Parameters.AddWithValue("@skipped", attachment.Skipped ? 1 : 0);
        cmd.Parameters.AddWithValue("@skipReason", (object?)attachment.SkipReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@extractedAt", attachment.ExtractedAt.ToString("O"));

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task DeleteAttachmentsForMessageAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM attachments WHERE message_id = @messageId";
        cmd.Parameters.AddWithValue("@messageId", messageId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Attachment>> GetSkippedAttachmentsAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, message_id, filename, file_path, size_bytes, content_type,
                   is_inline, skipped, skip_reason, extracted_at
            FROM attachments
            WHERE skipped = 1
            ORDER BY extracted_at DESC";

        var attachments = new List<Attachment>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            attachments.Add(ReadAttachment(reader));
        }

        return attachments;
    }

    private static Attachment ReadAttachment(SqliteDataReader reader)
    {
        return new Attachment
        {
            Id = reader.GetInt64(0),
            MessageId = reader.GetString(1),
            Filename = reader.GetString(2),
            FilePath = reader.GetString(3),
            SizeBytes = reader.GetInt64(4),
            ContentType = reader.IsDBNull(5) ? null : reader.GetString(5),
            IsInline = reader.GetInt32(6) != 0,
            Skipped = reader.GetInt32(7) != 0,
            SkipReason = reader.IsDBNull(8) ? null : reader.GetString(8),
            ExtractedAt = DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture)
        };
    }

    #endregion

    #region ZIP Extraction Operations

    /// <inheritdoc />
    public async Task<ZipExtraction?> GetZipExtractionAsync(long attachmentId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, attachment_id, message_id, zip_filename, extraction_path, extracted,
                   skip_reason, file_count, total_size_bytes, has_executables, has_unsafe_paths,
                   is_encrypted, extracted_at
            FROM zip_extractions
            WHERE attachment_id = @attachmentId";
        cmd.Parameters.AddWithValue("@attachmentId", attachmentId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadZipExtraction(reader);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<long> InsertZipExtractionAsync(ZipExtraction zipExtraction, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO zip_extractions (attachment_id, message_id, zip_filename, extraction_path, extracted,
                                         skip_reason, file_count, total_size_bytes, has_executables,
                                         has_unsafe_paths, is_encrypted, extracted_at)
            VALUES (@attachmentId, @messageId, @zipFilename, @extractionPath, @extracted,
                    @skipReason, @fileCount, @totalSizeBytes, @hasExecutables,
                    @hasUnsafePaths, @isEncrypted, @extractedAt);
            SELECT last_insert_rowid();";

        cmd.Parameters.AddWithValue("@attachmentId", zipExtraction.AttachmentId);
        cmd.Parameters.AddWithValue("@messageId", zipExtraction.MessageId);
        cmd.Parameters.AddWithValue("@zipFilename", zipExtraction.ZipFilename);
        cmd.Parameters.AddWithValue("@extractionPath", zipExtraction.ExtractionPath);
        cmd.Parameters.AddWithValue("@extracted", zipExtraction.Extracted ? 1 : 0);
        cmd.Parameters.AddWithValue("@skipReason", (object?)zipExtraction.SkipReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fileCount", zipExtraction.FileCount.HasValue ? zipExtraction.FileCount.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@totalSizeBytes", zipExtraction.TotalSizeBytes.HasValue ? zipExtraction.TotalSizeBytes.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@hasExecutables", zipExtraction.HasExecutables.HasValue ? (zipExtraction.HasExecutables.Value ? 1 : 0) : DBNull.Value);
        cmd.Parameters.AddWithValue("@hasUnsafePaths", zipExtraction.HasUnsafePaths.HasValue ? (zipExtraction.HasUnsafePaths.Value ? 1 : 0) : DBNull.Value);
        cmd.Parameters.AddWithValue("@isEncrypted", zipExtraction.IsEncrypted.HasValue ? (zipExtraction.IsEncrypted.Value ? 1 : 0) : DBNull.Value);
        cmd.Parameters.AddWithValue("@extractedAt", zipExtraction.ExtractedAt.ToString("O"));

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task DeleteZipExtractionsForMessageAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM zip_extractions WHERE message_id = @messageId";
        cmd.Parameters.AddWithValue("@messageId", messageId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ZipExtraction ReadZipExtraction(SqliteDataReader reader)
    {
        return new ZipExtraction
        {
            Id = reader.GetInt64(0),
            AttachmentId = reader.GetInt64(1),
            MessageId = reader.GetString(2),
            ZipFilename = reader.GetString(3),
            ExtractionPath = reader.GetString(4),
            Extracted = reader.GetInt32(5) != 0,
            SkipReason = reader.IsDBNull(6) ? null : reader.GetString(6),
            FileCount = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            TotalSizeBytes = reader.IsDBNull(8) ? null : reader.GetInt64(8),
            HasExecutables = reader.IsDBNull(9) ? null : reader.GetInt32(9) != 0,
            HasUnsafePaths = reader.IsDBNull(10) ? null : reader.GetInt32(10) != 0,
            IsEncrypted = reader.IsDBNull(11) ? null : reader.GetInt32(11) != 0,
            ExtractedAt = DateTimeOffset.Parse(reader.GetString(12), CultureInfo.InvariantCulture)
        };
    }

    #endregion

    #region ZIP Extracted File Operations

    /// <inheritdoc />
    public async Task<IReadOnlyList<ZipExtractedFile>> GetZipExtractedFilesAsync(long zipExtractionId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, zip_extraction_id, relative_path, extracted_path, size_bytes
            FROM zip_extracted_files
            WHERE zip_extraction_id = @zipExtractionId
            ORDER BY relative_path";
        cmd.Parameters.AddWithValue("@zipExtractionId", zipExtractionId);

        var files = new List<ZipExtractedFile>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            files.Add(ReadZipExtractedFile(reader));
        }

        return files;
    }

    /// <inheritdoc />
    public async Task InsertZipExtractedFileAsync(ZipExtractedFile file, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO zip_extracted_files (zip_extraction_id, relative_path, extracted_path, size_bytes)
            VALUES (@zipExtractionId, @relativePath, @extractedPath, @sizeBytes)";

        cmd.Parameters.AddWithValue("@zipExtractionId", file.ZipExtractionId);
        cmd.Parameters.AddWithValue("@relativePath", file.RelativePath);
        cmd.Parameters.AddWithValue("@extractedPath", file.ExtractedPath);
        cmd.Parameters.AddWithValue("@sizeBytes", file.SizeBytes);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task InsertZipExtractedFilesAsync(IEnumerable<ZipExtractedFile> files, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO zip_extracted_files (zip_extraction_id, relative_path, extracted_path, size_bytes)
            VALUES (@zipExtractionId, @relativePath, @extractedPath, @sizeBytes)";

        var zipExtractionIdParam = cmd.Parameters.Add("@zipExtractionId", SqliteType.Integer);
        var relativePathParam = cmd.Parameters.Add("@relativePath", SqliteType.Text);
        var extractedPathParam = cmd.Parameters.Add("@extractedPath", SqliteType.Text);
        var sizeBytesParam = cmd.Parameters.Add("@sizeBytes", SqliteType.Integer);

        foreach (var file in files)
        {
            zipExtractionIdParam.Value = file.ZipExtractionId;
            relativePathParam.Value = file.RelativePath;
            extractedPathParam.Value = file.ExtractedPath;
            sizeBytesParam.Value = file.SizeBytes;

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static ZipExtractedFile ReadZipExtractedFile(SqliteDataReader reader)
    {
        return new ZipExtractedFile
        {
            Id = reader.GetInt64(0),
            ZipExtractionId = reader.GetInt64(1),
            RelativePath = reader.GetString(2),
            ExtractedPath = reader.GetString(3),
            SizeBytes = reader.GetInt64(4)
        };
    }

    #endregion

    #region Folder Sync Progress Operations

    /// <inheritdoc />
    public async Task<FolderSyncProgress?> GetFolderSyncProgressAsync(string folderId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT folder_id, pending_next_link, pending_page_number, pending_message_index,
                   sync_started_at, last_checkpoint_at, messages_processed
            FROM folder_sync_progress
            WHERE folder_id = @folderId";
        cmd.Parameters.AddWithValue("@folderId", folderId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadFolderSyncProgress(reader);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task UpsertFolderSyncProgressAsync(FolderSyncProgress progress, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO folder_sync_progress (folder_id, pending_next_link, pending_page_number, pending_message_index,
                                              sync_started_at, last_checkpoint_at, messages_processed)
            VALUES (@folderId, @pendingNextLink, @pendingPageNumber, @pendingMessageIndex,
                    @syncStartedAt, @lastCheckpointAt, @messagesProcessed)
            ON CONFLICT(folder_id) DO UPDATE SET
                pending_next_link = @pendingNextLink,
                pending_page_number = @pendingPageNumber,
                pending_message_index = @pendingMessageIndex,
                last_checkpoint_at = @lastCheckpointAt,
                messages_processed = @messagesProcessed";

        cmd.Parameters.AddWithValue("@folderId", progress.FolderId);
        cmd.Parameters.AddWithValue("@pendingNextLink", (object?)progress.PendingNextLink ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pendingPageNumber", progress.PendingPageNumber);
        cmd.Parameters.AddWithValue("@pendingMessageIndex", progress.PendingMessageIndex);
        cmd.Parameters.AddWithValue("@syncStartedAt", progress.SyncStartedAt.HasValue ? progress.SyncStartedAt.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("@lastCheckpointAt", progress.LastCheckpointAt.HasValue ? progress.LastCheckpointAt.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("@messagesProcessed", progress.MessagesProcessed);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteFolderSyncProgressAsync(string folderId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM folder_sync_progress WHERE folder_id = @folderId";
        cmd.Parameters.AddWithValue("@folderId", folderId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FolderSyncProgress>> GetAllFolderSyncProgressAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT folder_id, pending_next_link, pending_page_number, pending_message_index,
                   sync_started_at, last_checkpoint_at, messages_processed
            FROM folder_sync_progress
            ORDER BY sync_started_at";

        var results = new List<FolderSyncProgress>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadFolderSyncProgress(reader));
        }

        return results;
    }

    private static FolderSyncProgress ReadFolderSyncProgress(SqliteDataReader reader)
    {
        return new FolderSyncProgress
        {
            FolderId = reader.GetString(0),
            PendingNextLink = reader.IsDBNull(1) ? null : reader.GetString(1),
            PendingPageNumber = reader.GetInt32(2),
            PendingMessageIndex = reader.GetInt32(3),
            SyncStartedAt = reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
            LastCheckpointAt = reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
            MessagesProcessed = reader.GetInt32(6)
        };
    }

    #endregion

    #region IDisposable

    private readonly object _disposeLock = new();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        lock (_disposeLock)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        _connection?.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal errors - connection may be in an invalid state
                    }
                    _connection = null;
                }
                _disposed = true;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Capture the connection reference under lock to avoid race condition
        SqliteConnection? connectionToDispose = null;

        lock (_disposeLock)
        {
            if (!_disposed)
            {
                connectionToDispose = _connection;
                _connection = null;
                _disposed = true;
            }
        }

        // Dispose outside the lock to avoid blocking
        if (connectionToDispose != null)
        {
            try
            {
                await connectionToDispose.DisposeAsync();
            }
            catch
            {
                // Ignore disposal errors - connection may be in an invalid state
            }
        }

        GC.SuppressFinalize(this);
    }

    #endregion
}
