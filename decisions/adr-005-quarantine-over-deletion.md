# ADR-005: Quarantine Over Deletion

## Status

Accepted

## Context

During incremental sync operations, the application must handle messages that have been deleted from the Microsoft 365 mailbox since the last sync. Microsoft Graph API delta queries return deleted messages with an `@removed` annotation.

Scenarios where messages are deleted include:

- User manually deletes messages in Outlook/OWA
- Messages moved to Deleted Items folder (not permanently deleted)
- Messages moved between folders (shows as delete + create)
- Retention policies automatically delete old messages
- Accidental bulk deletion

The tool must decide how to handle the local copy of deleted messages. Options include mirroring deletion (delete local files), ignoring deletion (keep local files forever), or quarantining (move to separate location for review).

Design considerations include:

- **Data safety**: Prevent accidental data loss
- **Reversibility**: Allow recovery from mistaken deletions
- **User control**: Give users final say on permanent deletion
- **Sync integrity**: Maintain clear separation between active and deleted content

## Decision

Never automatically delete local files when messages are deleted in M365. Instead, move deleted messages to a quarantine folder that preserves the original folder structure. Users manually review and permanently delete quarantined content when ready.

## Rationale

### Why Quarantine Instead of Immediate Deletion

**Accidental deletion protection**: Users may accidentally delete important messages, have retention policies delete messages they wanted to keep, or experience bugs/sync issues that incorrectly mark messages as deleted. Quarantine provides a safety net where deleted messages are moved but not permanently lost.

**Review before permanent deletion**: Users can browse quarantined messages to verify deletion was intentional, restore messages by manually moving files back, permanently delete quarantined content on their own schedule, or keep quarantine indefinitely as extended archive.

**Asymmetric trust model**: The archive serves as a backup. Trust the source (M365) for what exists, but don't trust deletions without user confirmation. The local archive may be more complete than the cloud mailbox.

**Regulatory/compliance scenarios**: Some users may need to retain deleted messages for legal or compliance reasons. Automatic deletion would prevent this.

### Why Not Mirror M365 Deletion

**Approach**: Delete local files immediately when M365 reports deletion.

**Rejected because**: Irreversible (once deleted, messages can only be recovered from separate backups), no confirmation (user has no opportunity to review before permanent deletion), accidental loss (bugs, API issues, or user mistakes become permanent), and compliance risk (may violate retention requirements).

### Why Not Ignore Deletions

**Approach**: Keep all local files forever, never delete anything.

**Rejected because**: Folder moves become invisible (message moved between folders appears in both locations), duplicates accumulate (folder reorganizations create many duplicates), no cleanup mechanism (archive grows indefinitely with obsolete content), and sync state confusion (database shows message deleted but file still exists).

### Why Not Mark as Deleted In Place

**Approach**: Keep files in original location but mark them as deleted (rename, flag file, database field).

**Rejected because**: Visual clutter (deleted messages mix with active messages), accidental access (users may accidentally open deleted messages), backup confusion (backups include "deleted" content without clear separation), and transformation complexity (HTML/Markdown outputs need deletion markers too).

## Consequences

### Positive

- **Data safety**: Deleted messages are preserved, not lost
- **Reversibility**: Users can restore accidentally deleted messages
- **User control**: Users decide when to permanently delete
- **Compliance-friendly**: Supports retention requirements
- **Clear separation**: Active vs deleted content is visually distinct
- **Audit trail**: Database records when and why messages were quarantined

### Negative

- **Storage growth**: Quarantine consumes disk space until manually cleaned
- **Manual process**: Users must remember to review and clean quarantine
- **Extra step**: Adds administrative overhead for users who want auto-deletion

### Neutral

- **Different paradigm**: Not a true "mirror" of M365 (intentionally asymmetric)
- **User education**: Users must understand quarantine concept and workflow

## Alternatives Considered

All alternatives detailed in Rationale section above.

## References

- Microsoft Graph Delta Query: https://learn.microsoft.com/en-us/graph/delta-query-messages
- Delta Query @removed Annotation: https://learn.microsoft.com/en-us/graph/delta-query-overview#resource-representation-in-the-delta-query-response
- See [DESIGN.md](../DESIGN.md) for implementation details (quarantine directory structure, database tracking, user workflow)
