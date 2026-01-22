# ADR-008: Streaming Sync with Mini-Batch Checkpointing

## Status

Accepted

## Context

The application downloads email messages from Microsoft 365 mailboxes that can contain tens of thousands to millions of messages. The initial batch-based sync approach had several issues:

**User experience concerns**: Users running the sync utility want immediate visual feedback that the tool is working correctly. With batch processing, the user would see no file creation until an entire batch completed, creating uncertainty about whether the sync was progressing or stuck. For long-running syncs that may take hours, seeing messages appear immediately and incrementally from the start provides confidence that the tool is functioning as expected.

**Memory constraints**: Loading all messages for a folder into memory before processing created memory pressure for large mailboxes. Mailboxes with very large folders could exhaust available memory, causing crashes or forcing artificial limits on folder sizes.

**Interruption recovery**: Batch processing required re-downloading entire batches when sync was interrupted mid-way. If the user cancelled the operation or the system crashed, progress within a batch was lost. For folders with thousands of messages, this meant potentially re-downloading hundreds of messages.

The sync engine needed an architecture that provided immediate feedback, bounded memory usage, and fine-grained resumption after interruptions.

## Decision

Implement streaming sync that processes messages page-by-page as Microsoft Graph delta query results arrive, with mini-batch checkpointing at configurable intervals.

Messages are downloaded and transformed immediately as each delta page is received from the Graph API, rather than collecting all messages first. Progress is persisted to the database after each mini-batch, enabling resumption from the exact page and message position if interrupted.

## Rationale

### Why Streaming Over Batch Loading

**Immediate user feedback**: Files appear on disk as soon as the first delta page is processed. Users see the sync working immediately, even on large mailboxes that take hours to complete. This is particularly valuable for a CLI tool where the user is actively watching output.

**Bounded memory usage**: Only one page of message metadata is held in memory at a time. This scales to any mailbox size without memory constraints, as the working set is constant regardless of total message count.

**Efficient network usage**: Processing overlaps with network I/O - while one page is being processed, the next can be fetched. This improves overall throughput compared to "fetch all then process" approaches.

### Why Mini-Batch Checkpoints Over Per-Message

**Database overhead balance**: Writing a checkpoint after every single message would multiply database writes significantly (potentially millions of writes for large mailboxes). Mini-batches (configurable via `CheckpointInterval`) balance reliability against database overhead.

**Practical resumption**: The worst-case loss on interruption is one mini-batch (default: a few messages), not an entire folder. This provides good enough resumption granularity for practical use.

**Transaction efficiency**: Mini-batches can be processed within reasonable transaction scopes, ensuring atomic completion of small groups of messages.

### Why Not Per-Page Checkpoints Only

Checkpointing only at page boundaries (typically 50-100 messages) would lose too much progress on interruption for large folders with many pages. Mini-batch checkpointing within pages provides finer granularity.

## Consequences

### Positive

- **Immediate user feedback**: Files appear on disk from the first page, providing confidence the sync is working
- **Bounded memory usage**: Scales to any mailbox size without memory constraints
- **Fine-grained resumption**: Interrupted syncs resume from the exact mini-batch position, minimizing re-downloads
- **Overlapped I/O**: Processing and network fetching can overlap for better throughput
- **Inline transformation**: Messages can be transformed immediately after download, creating output files incrementally

### Negative

- **Increased database writes**: Mini-batch checkpointing adds database I/O compared to folder-level tracking only
- **Implementation complexity**: Streaming with checkpointing is more complex than simple batch processing
- **Schema migration required**: New `folder_sync_progress` table required (schema version 2)

### Neutral

- **Configurable checkpoint granularity**: Users can tune `CheckpointInterval` based on their reliability vs. performance preferences
- **Compatible with existing delta tokens**: The streaming approach still uses Graph API delta tokens for incremental sync, just processes them page-by-page

## References

- Microsoft Graph Delta Query: https://learn.microsoft.com/en-us/graph/delta-query-overview
- See [DESIGN.md](../DESIGN.md) for implementation details (database schema, sync flow diagrams)
