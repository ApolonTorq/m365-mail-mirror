# ADR-004: SQLite for State Tracking

## Status

Accepted

## Context

The application requires persistent state storage for:

- **Sync progress**: Last sync timestamp, batch checkpoints, folder enumeration state
- **Message tracking**: Which messages have been downloaded, their local paths, metadata
- **Folder mapping**: M365 folder IDs to local directory paths
- **Transformation state**: Which transformations have been applied to each message
- **Incremental sync**: Tracking what needs to be synced on subsequent runs

State storage requirements include:

- **Portability**: State should travel with the mail archive
- **Queryability**: Need to find messages by ID, date, folder, transformation status
- **Transaction support**: Batch updates must be atomic
- **Crash recovery**: Partially-completed operations should not corrupt state
- **Simple deployment**: No external database server required
- **Cross-platform**: Works on Windows, macOS, Linux

## Decision

Use SQLite as the state database, stored in the mail archive root directory. The database stores metadata only (file paths, message metadata, sync state) - never message content.

## Rationale

### Why SQLite

**File-based, zero configuration**: SQLite is embedded directly in the application. No separate database process to install, configure, or manage. The entire database is a single file.

**Travels with data**: By co-locating the database in the mail archive root, moving the archive folder preserves all state, backups include both mail and metadata, no separate database backup is required, and multiple archives can coexist independently.

**Transaction support**: SQLite provides ACID transactions ensuring batch message downloads are atomic, crashes during sync don't corrupt state, and failed operations roll back cleanly.

**SQL queryability**: Standard SQL enables finding messages by folder/date/sender, checking transformation status across messages, filtering messages needing reprocessing, and generating sync statistics.

**Cross-platform**: SQLite works identically on all platforms with consistent file format. An archive created on Windows can be moved to Linux without conversion.

**Well-supported**: Mature .NET library (`Microsoft.Data.Sqlite`) with excellent documentation.

**Performance**: For this use case (hundreds to millions of messages), indexing provides fast lookups, batch inserts are efficient, and file size remains reasonable (metadata only, not message bodies).

### Why Not JSON Files

**Approach**: Store state as JSON files (one per folder, or one master file).

**Rejected because**: No transactional updates (crash during write corrupts entire file), poor queryability (must load entire file into memory and filter), concurrency issues, schema evolution is manual, and performance degrades with large files.

### Why Not Separate Database Server

**Approach**: Use PostgreSQL, MySQL, or other server-based database.

**Rejected because**: Deployment complexity (users must install and configure database server), separate backup (database and mail archive are separate entities), portability issues (can't simply copy folder to move archive), overkill (this use case doesn't need client-server capabilities), and requires running database process.

### Why Not NoSQL Database

**Approach**: Use embedded NoSQL (LevelDB, RocksDB) or document store (MongoDB).

**Rejected because**: Query complexity (message filtering requires application-level logic), less mature .NET support (fewer production-tested libraries), overkill (don't need horizontal scaling or eventual consistency), and SQL familiarity (SQLite uses standard SQL, widely understood).

### Why Co-locate in Archive Root

**Approach**: Store database in user's home directory or app config folder.

**Rejected because**: Separation from data (moving archive breaks association with state), multiple archives (can't easily manage multiple independent archives), backup fragmentation (must backup database and archive separately), and path dependencies (database must store absolute paths, brittle across systems).

## Consequences

### Positive

- **Portability**: Entire archive + state is a single directory
- **Simple deployment**: No database server installation
- **Transaction safety**: Crash-resistant state updates
- **SQL power**: Flexible querying without custom indexing code
- **Cross-platform**: Identical behavior on Windows, macOS, Linux
- **Tooling**: Can inspect state using any SQLite browser
- **Backup simplicity**: Copy directory to backup everything
- **Performance**: Fast enough for millions of messages
- **Metadata only**: Database never duplicates message content from EML files

### Negative

- **Single-user**: SQLite isn't designed for concurrent access (not an issue for this use case)
- **File size**: Grows with message count (typically ~1KB per message metadata row)
- **Corruption risk**: File corruption affects all state (mitigated by WAL mode)

### Neutral

- **SQL knowledge**: Developers must understand SQL for schema changes
- **Migration complexity**: Schema evolution requires migration scripts
- **Embedded vs server**: Different trade-offs than client-server database

## Alternatives Considered

All alternatives detailed in Rationale section above.

## References

- SQLite Official Site: https://sqlite.org/
- SQLite in .NET: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/
- Microsoft.Data.Sqlite: https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlite
- See [DESIGN.md](../DESIGN.md) for implementation details (schema, transaction strategy, migration approach)
