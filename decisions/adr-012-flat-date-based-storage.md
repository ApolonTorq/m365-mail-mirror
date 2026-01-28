# ADR-012: Flat Date-Based Storage

## Status

Accepted

## Context

The m365-mail-mirror tool archives Microsoft 365 mailboxes to local storage. The Microsoft Graph API organizes messages into folders (Inbox, Sent Items, Archive, user-created folders, etc.), and the current implementation replicates this folder hierarchy in the local storage structure.

This creates several challenges:

**Move tracking complexity**: Users frequently move messages between folders in Outlook for organization purposes. The current implementation detects these moves via delta sync and must physically relocate EML files and update database records accordingly.

**Sync complexity**: Delta queries return moved messages with `@removed reason="changed"`, requiring special handling to detect the new folder location and relocate files. This adds significant code complexity and potential for errors.

**No benefit for primary use cases**: The tool's primary purposes are backup and providing email content for AI agents. Neither use case requires folder organization on disk:
- **Backup**: Users need message preservation, not folder mirroring
- **AI agents**: Benefit from flat, predictable file structures over nested hierarchies

**Folder structure is metadata, not data**: The folder a message resides in is organizational metadata that can change over time. The message content itself is immutable.

## Decision

Store all email messages in a flat date-based structure: `eml/{YYYY}/{MM}/{filename}.eml`

Key aspects of this decision:

1. **Remove folder path from storage paths**: Messages are stored by received date only, not by M365 folder
2. **Continue traversing M365 folders via API**: The Graph API requires folder-by-folder enumeration for delta sync; we traverse folders but don't replicate the structure
3. **Keep folder path as database metadata**: Store `folder_path` in the messages table for filtering, display, and querying
4. **Ignore message moves**: When delta sync reports a moved message, treat it as a no-op (message already exists locally)
5. **Remove file move capability**: Delete the `MoveEmlAsync()` method entirely

The same structure applies to transformed outputs: `transformed/{YYYY}/{MM}/{filename}.html`

### Filename Convention

To support browsing flat storage with meaningful ordering, filenames include folder and datetime prefixes:

**Pattern**: `{folder-prefix}_{YYYY-MM-DD-HH-MM-SS}_{sanitized-subject}.eml`

**Components**:
- **folder-prefix**: Lowercase M365 folder path with nested levels joined by `-` (e.g., "inbox-processed" for "Inbox/Processed")
- **datetime**: Full timestamp using `-` separators for sortability (e.g., "2024-01-15-10-30-45")
- **sanitized-subject**: Message subject with spaces and illegal characters replaced by `-`, in lowercase

**Separator convention**:
- `_` (underscore) separates major filename components
- `-` (dash) is used within components (folder levels, datetime parts, subject words)

**Example filenames**:
```
inbox_2024-01-15-10-30-45_meeting-notes.eml
inbox-processed_2024-01-15-14-00-00_re-weekly-status-report.eml
sent-items_2024-01-15-09-00-00_project-update.eml
```

**Benefits**:
- Alphabetical sorting groups files by source folder
- Within each folder group, files sort chronologically by received time
- Human-readable filenames for manual archive browsing
- Folder organization visible at filesystem level without requiring database queries

## Rationale

### Simplifies sync logic

Without move tracking, the sync engine only needs to handle three states:
- **New message**: Download and store
- **Deleted message**: Quarantine the file
- **Existing message**: Skip (already have it)

Moved messages become identical to existing messages—the file is already stored and doesn't need to change.

### Messages are immutable

Once an email is received, its content never changes. The folder it's organized into is a user preference that can change repeatedly, but the message itself is fixed. Storing by received date reflects this immutability.

### Reduces filesystem operations

Incremental syncs no longer trigger file moves. This improves sync performance and reduces potential for file system errors during reorganization operations.

### Backup-appropriate model

An archive is a point-in-time backup, not a live mirror. The folder structure at any given moment is ephemeral; the message content is permanent. Storing by date captures the permanent aspect.

### AI-agent friendly

Flat, predictable directory structures are easier for automated tools to traverse. Agents don't need to understand or navigate folder hierarchies—they can simply iterate through year/month directories.

### Maintains queryability

Folder information remains available via database queries. Users can still filter messages by folder path, view folder statistics, or export by folder. The information is preserved as metadata, just not as physical directory structure.

## Consequences

### Positive

- **Simpler codebase**: Remove move detection, move processing, and file relocation logic
- **Faster incremental sync**: No file moves during delta sync operations
- **More robust**: Fewer moving parts means fewer potential failure modes
- **Predictable structure**: Easy to reason about where files will be stored
- **AI-friendly**: Automated tools can traverse the archive without understanding folder semantics

### Negative

- **Folder structure not visible on disk**: Users cannot browse by folder in the filesystem; must use database queries or index pages
- **Existing archives require migration**: Archives created with the old folder structure won't automatically reorganize
- **Slight increase in filename collisions**: Messages from different folders with same subject/time now share the same directory (mitigated by existing collision handling)

### Neutral

- **Delta sync still required**: Must still traverse folders via API and process delta queries; the complexity is in interpretation, not elimination
- **Folder-based features unchanged**: Status command folder statistics, index generation by folder for display purposes, etc. still work via database

## Alternatives Considered

### Keep folder structure (current implementation)

**Approach**: Continue replicating M365 folder hierarchy in local storage.

**Rejected because**: Adds significant complexity for move tracking with no benefit for backup or AI use cases. The folder structure is organizational metadata that changes over time; storing it as physical structure creates synchronization burden.

### Store by Graph immutable ID

**Approach**: Use the message's immutable ID (or hash thereof) as the filename/path.

**Rejected because**: Loses human-readable organization entirely. Browsing the archive manually becomes impractical. The date-based structure provides natural chronological organization that humans can understand.

### Make folder structure configurable

**Approach**: Add a configuration option to choose between folder-based and flat storage.

**Rejected because**: Creates two code paths to maintain and test. Increases complexity without clear benefit—if folder structure isn't needed for primary use cases, supporting it optionally still requires the complex move-tracking code.

### Store folders as symlinks

**Approach**: Use flat storage but create symbolic links in a folder structure pointing to actual files.

**Rejected because**: Platform-specific (symlinks work differently on Windows vs Unix), adds complexity, and doesn't solve the move-tracking problem (would still need to update symlinks when messages move).

## References

- See [DESIGN.md](../DESIGN.md) for storage design details and directory structure documentation
- See [ADR-003](adr-003-eml-first-storage-with-transformations.md) for the EML-first storage decision this builds upon
- Microsoft Graph Delta Query: https://learn.microsoft.com/en-us/graph/delta-query-messages
