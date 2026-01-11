# m365-mail-mirror Specification

A command-line tool to mirror Microsoft 365 mailbox to local HTML files for archival and offline access.

## Overview

m365-mail-mirror synchronizes email messages from a Microsoft 365 mailbox to a local filesystem, storing messages as human-readable HTML files with email-client-style formatting. The tool is designed for archival purposes, providing a reliable local backup of cloud-hosted mail.

## Technology Stack

- **Runtime**: .NET 10
- **Language**: C#
- **CLI Framework**: [CliFx](https://github.com/Tyrrrz/CliFx) for command-line parsing and command structure
- **Distribution**:
  - `dotnet tool install` for developers with .NET SDK
  - Self-contained single-file executables for end users (Windows, macOS, Linux)
- **Dependencies**:
  - Microsoft Graph SDK for .NET
  - CliFx for CLI commands and options
  - SQLite (via Microsoft.Data.Sqlite or similar)
  - YAML parser (YamlDotNet or similar)

## Authentication

### Method

- **Device Code Flow** only (interactive authentication)
- User visits a URL, enters a code, grants consent
- Works with personal Microsoft accounts and work/school accounts

### App Registration

- Users must register their own Azure AD application
- Documentation will provide step-by-step registration instructions
- Required API permission: `Mail.Read` (delegated, minimal scope)

### Token Storage

- Refresh tokens stored in OS credential store:
  - Windows: Credential Manager
  - macOS: Keychain
  - Linux: Secret Service (libsecret)
- Tokens refreshed automatically during long sync operations

### Auth Failure Handling

- On token refresh failure in unattended mode: write warning to stderr, exit with error code
- User must re-authenticate interactively on next run

## Storage Format

### Directory Structure

```text
<mail-root>/
├── .sync.db                          # SQLite state database
├── style.css                         # (copied to each folder)
├── Inbox/
│   ├── style.css
│   ├── 2024/
│   │   ├── 01/
│   │   │   ├── style.css
│   │   │   ├── Meeting_Notes_1030.html
│   │   │   ├── Meeting_Notes_1030_attachments/
│   │   │   │   └── document.pdf
│   │   │   ├── Re_Project_Update_1415.html
│   │   │   └── Re_Project_Update_1415_attachments/
│   │   │       └── image.png
│   │   └── 02/
│   │       └── ...
│   └── Subfolder/
│       └── 2024/
│           └── ...
├── Sent Items/
│   └── 2024/
│       └── ...
├── _Quarantine/                      # Deleted items quarantine
│   └── Inbox/                        # Preserves original structure
│       └── 2024/
│           └── 01/
│               └── ...
└── _Errors/                          # Malformed message quarantine
    └── <message-id>/
        └── raw-response.json
```

### File Naming

- **HTML files**: `<sanitized-subject>_<HHMM>.<ext>`
  - Subject sanitized: illegal filesystem characters replaced with underscore
  - Time appended for uniqueness within same YYYY/MM folder
  - If duplicate subject+time exists, append additional time precision or counter
  - Maximum filename length: dynamically calculated based on current path depth to stay within OS limits (260 chars on Windows)
- **Plain text emails**: Stored as `.txt` files (not converted to HTML)
- **HTML emails**: Stored as `.html` files
- **Attachments**: Sibling folder named `<message-filename>_attachments/`

### HTML Format

#### Template Structure

```html
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8">
    <link rel="stylesheet" href="style.css">
    <title>Subject Line</title>
</head>
<body>
    <header class="email-header">
        <div class="field"><span class="label">From:</span> sender@example.com</div>
        <div class="field"><span class="label">To:</span> recipient@example.com</div>
        <div class="field"><span class="label">Cc:</span> cc@example.com</div>
        <div class="field"><span class="label">Date:</span> January 15, 2024 10:30 AM</div>
        <div class="field"><span class="label">Subject:</span> Meeting Notes</div>
    </header>
    <nav class="thread-nav">
        <a href="../12/Original_Message_0930.html">← In reply to</a>
        <a href="Re_Re_Meeting_Notes_1600.html">Reply →</a>
    </nav>
    <main class="email-body">
        <!-- Original email HTML body, scripts stripped -->
    </main>
    <footer class="attachments">
        <h3>Attachments</h3>
        <ul>
            <li><a href="Meeting_Notes_1030_attachments/document.pdf">document.pdf</a></li>
        </ul>
    </footer>
</body>
</html>
```

#### Styling

- **Default**: External `style.css` file copied to each folder
- **Optional**: `--inline-styles` flag embeds CSS directly in HTML files
- Email-client-style header presentation (similar to Outlook/Gmail print view)

#### HTML Processing

- **Scripts**: All `<script>` tags stripped for security
- **External images**: Preserved as-is by default
  - `--strip-external-images` flag removes external `<img>` tags
- **Malformed HTML**: Passed through as-is (no sanitization beyond script removal)

#### Thread Navigation

- HTML includes links to parent (In-Reply-To) and child (replies) messages
- **Deferred linking**: If referenced message not yet synced, placeholder inserted; updated when target message is synced later
- Links are relative paths within the archive structure

#### Recipients Display

- Shows From, To, CC, BCC (when available)
- `--hide-cc` flag: omit CC from header
- `--hide-bcc` flag: omit BCC from header

## Sync Behavior

### Initial Sync

- **Batch-based**: Process N messages at a time (configurable via `--batch-size`, default 100)
- Progress checkpointed after each batch
- Resumable from last completed batch on interruption

### Incremental Sync

- **Date-based catchup**: Track last successful sync timestamp, query messages newer than that
- **Configurable overlap**: Re-check messages from N minutes before last sync (default 60 minutes, configurable)
- Overlapping messages simply overwrite existing files (idempotent)

### Folder Handling

- **Mirrored structure**: Local folders match M365 hierarchy exactly
- **Date subfolders**: Within each folder, messages organized by `YYYY/MM/` based on received date
- **Folder moves**: When message moves between folders in M365, file is physically moved to new location locally
- **Folder exclusions**: Glob patterns (e.g., `Junk*`, `**/Spam`)

### Default Exclusions

- `Junk Email`
- `Deleted Items`

Users can override defaults via config.

### Deletion Handling

- **Never delete local files** when message deleted in M365
- Deleted messages moved to `_Quarantine/` folder preserving original structure
- Quarantine folder does not participate in future syncs
- User manually reviews and deletes quarantine contents

### Message Updates

- **Immutable once downloaded**: Local copy never updated for read status, flags, etc.
- Only downloaded once; subsequent syncs skip already-synced messages

### Malformed Messages

- **Quarantine**: Save raw API response to `_Errors/<message-id>/raw-response.json`
- Continue sync, log error, report count at end

## API Interaction

### Rate Limiting

- **Adaptive pacing**: Monitor Graph API quota headers, proactively slow requests before hitting limits
- Respect `Retry-After` header when 429 received
- Display current pacing state in progress bar

### Parallelism

- Configurable concurrent requests: `--parallel N` (default 5)
- Downloads multiple messages simultaneously

### Mailbox Scope

- Default: authenticated user's primary mailbox
- `--mailbox <email>` flag: sync specified shared/delegated mailbox instead
- Separate sync runs for different mailboxes

## Data Verification

### During Sync

- **Size check**: Verify downloaded content size matches API-reported size
- Mismatches trigger re-download attempt, then quarantine if still failing

### Atomic Writes

- Messages written to temp file first, renamed on completion
- Prevents partial files on interruption

### Verify Subcommand

- Scan local store for integrity issues:
  - Missing files referenced in database
  - Orphaned database entries (no corresponding file)
  - Files not tracked in database
- **Auto-fix safe issues**: Remove orphaned DB entries, flag missing files for re-sync

## State Database (SQLite)

### Location

- `.sync.db` in mail root directory (travels with the data)

### Schema (conceptual)

```sql
-- Sync state
CREATE TABLE sync_state (
    mailbox TEXT PRIMARY KEY,
    last_sync_time TEXT,
    last_batch_id INTEGER
);

-- Message tracking
CREATE TABLE messages (
    graph_id TEXT PRIMARY KEY,
    local_path TEXT NOT NULL,
    folder_path TEXT NOT NULL,
    subject TEXT,
    sender TEXT,
    received_time TEXT,
    size INTEGER,
    has_attachments INTEGER,
    in_reply_to TEXT,
    thread_links_updated INTEGER DEFAULT 0
);

-- Folder mapping
CREATE TABLE folders (
    graph_id TEXT PRIMARY KEY,
    local_path TEXT NOT NULL,
    display_name TEXT
);
```

### Queryable Metadata

- From, To, Subject, Date stored for filtering
- No full-text search indexing (not FTS)

## CLI Interface

### Commands

```text
m365-mail-mirror sync [options]
m365-mail-mirror sync --dry-run [options]
m365-mail-mirror status
m365-mail-mirror verify [--fix]
m365-mail-mirror auth login
m365-mail-mirror auth logout
m365-mail-mirror auth status
m365-mail-mirror auth refresh
```

### Global Options

```text
--config <path>       Path to config file (default: ~/.config/m365-mail-mirror/config.yaml)
--quiet               Suppress output except errors
--verbose             Detailed logging
--log-file <path>     Write logs to file (opt-in)
```

### Sync Options

```text
--mailbox <email>     Sync specified mailbox instead of primary
--output <path>       Local directory for mail storage (required on first run)
--batch-size <n>      Messages per batch (default: 100)
--parallel <n>        Concurrent downloads (default: 5)
--exclude <pattern>   Glob pattern for folders to skip (can specify multiple)
--overlap <minutes>   Minutes of overlap for incremental sync (default: 60)
--dry-run             Show what would be synced without downloading
--inline-styles       Embed CSS in HTML instead of external stylesheet
--strip-external-images  Remove external image references
--hide-cc             Omit CC from HTML headers
--hide-bcc            Omit BCC from HTML headers
```

### Configuration File (YAML)

Location: `~/.config/m365-mail-mirror/config.yaml` (or specified via `--config`)

```yaml
# Azure AD app registration
client_id: "your-app-client-id"
tenant_id: "common"  # or specific tenant

# Sync settings
output: "/path/to/mail/archive"
mailbox: "user@example.com"  # optional, defaults to primary
batch_size: 100
parallel: 5
overlap_minutes: 60

# Folder filtering
exclude:
  - "Junk Email"
  - "Deleted Items"
  - "Archive/Old/*"

# Display options
inline_styles: false
strip_external_images: false
hide_cc: false
hide_bcc: false
```

### Configuration Precedence

1. CLI flags (highest priority)
2. Environment variables (`M365_MIRROR_*` prefix)
3. Config file (lowest priority)

## Output and Logging

### Default Output (Progress Bar + Summary)

```text
Syncing mailbox: user@example.com
[████████████░░░░░░░░] 60% | 150/250 messages | 45MB/75MB | Inbox/2024/01
Rate: 12 msg/s | Pacing: normal

Sync complete:
  Messages synced: 250
  Folders synced: 15
  Total size: 75MB
  Errors: 2 (see _Errors/)
  Time elapsed: 2m 30s
```

### Quiet Mode

- No output on success
- Only errors written to stderr

### Verbose Mode

- Per-message logging
- Pacing/throttling decisions
- API request details

### Log File (opt-in)

- Plain text format
- Written to specified path
- Includes timestamps and log levels

## Error Handling

### Disk Full

- Exit immediately with error code
- Preserve what was successfully synced
- Partial temp files cleaned up (atomic writes)

### Network Errors

- Retry with exponential backoff (respect Retry-After)
- After max retries, checkpoint progress and exit
- Resume from checkpoint on next run

### Concurrency

- Lock file prevents multiple simultaneous syncs
- Second process exits immediately with "sync in progress" error

### Exit Codes

- `0`: Success (full or partial sync completed)
- `1`: Error (auth failure, network error, disk full, etc.)

## Platform Support

### Primary Targets

- Windows 10/11 (x64, ARM64)
- macOS (Intel, Apple Silicon)
- Linux (x64, ARM64)

### Filesystem Considerations

- Path sanitization: Replace `? * : " < > |` with underscore
- Windows path limit: Dynamic filename truncation to stay within 260 chars
- UTF-8 filenames supported (respecting platform normalization)

## Security Considerations

### Data at Rest

- No encryption of local files (user's responsibility)
- Tokens in OS credential store (platform-encrypted)

### Network

- All API communication over HTTPS
- No sensitive data in command-line arguments visible in process list

### Scripts in Email

- All `<script>` tags stripped from HTML output
- Prevents XSS when viewing archived emails in browser

## Future Considerations (Out of Scope for v1)

- Watch mode (continuous sync)
- Full-text search indexing
- Export to other formats (mbox, PDF)
- Multiple profile support
- Calendar/contacts sync
- Push notifications for new mail

## Documentation Requirements

- README with comprehensive examples:
  - First-time setup and Azure AD app registration
  - Initial sync walkthrough
  - Cron/Task Scheduler setup
  - Recovery from common errors
  - Configuration examples
- `--help` text for all commands and options
- Troubleshooting guide for common issues
