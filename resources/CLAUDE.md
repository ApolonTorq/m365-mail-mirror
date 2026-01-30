# Mirrored Mailbox Archive - Guide for Claude Code

This folder contains a Microsoft 365 mailbox that has been archived locally with multiple query-friendly formats.

## Quick Overview

The primary data you'll work with:

- **Markdown files** (`transformed/YYYY/MM/*.md`) - AI-optimized email content with structured metadata
- **Attachments** (`transformed/YYYY/MM/attachments/`) - Extracted files from emails
- **SQLite database** (`status/.sync.db`) - Fast metadata queries (sender, recipient, dates, folders)
- **HTML indexes** (`transformed/YYYY/MM/index.html`) - Browsable navigation of messages by month

The `eml/` folder contains the canonical source format and **must not be modified**. Access only if user explicitly requests it.

## ⚠️ Read-Only Folders

The following folders are **read-only** and **must never be modified**:

- **`eml/`** - Canonical EML archive (source of truth from Microsoft 365)
- **`status/`** - SQLite database and sync state metadata
- **`transformed/`** - Generated output (Markdown, HTML, attachments)

These folders are managed by `m365-mail-mirror` commands. Any manual modifications will corrupt the archive and break sync operations. If you need to modify email content, regenerate transformations via the tool instead.

## Directory Structure

```
<archive-root>/
├── CLAUDE.md                         ← You are here
├── status/
│   └── .sync.db                      # Metadata database
├── transformed/                      # PRIMARY FOLDER FOR QUERIES
│   └── YYYY/MM/
│       ├── *.md                      # Markdown email files (use these)
│       ├── index.html                # Month overview (browsable HTML navigation)
│       ├── images/                   # Inline images from emails
│       └── attachments/              # Extracted files
│           └── {message}_attachments/
│               └── {filename}
├── eml/                              # Source format (avoid unless requested)
│   └── YYYY/MM/*.eml
└── _Quarantine/                      # Deleted messages
    └── eml/YYYY/MM/*.eml
```

## File Organization

Messages are organized by **received date** in `YYYY/MM/` folders:

```
transformed/
├── 2024/
│   ├── 01/  # January 2024
│   │   ├── inbox_2024-01-15-10-30-45_meeting-notes.md
│   │   ├── archive-budget_2024-01-15-14-15-22_project-update.md
│   │   └── index.html
│   └── 02/  # February 2024
│       └── inbox_2024-02-10-09-00-15_monthly-report.md
```

**File naming**: `{folder-prefix}_{YYYY-MM-DD-HH-MM-SS}_{sanitized-subject}.md`

- **Folder prefix**: Original M365 folder (e.g., "Inbox" → `inbox`, "Archive/Budget" → `archive-budget`), max 30 chars, lowercase with dashes
- **Timestamp**: Full datetime to second resolution (e.g., `2024-01-15-10-30-45`)
- **Subject**: Sanitized subject line, lowercase with dashes, max ~50 chars
- Falls back to `no-subject` if subject is empty after sanitization

## Markdown Format (Primary Query Source)

**Location**: `transformed/YYYY/MM/*.md`

**Structure**: YAML front matter + Markdown body

```markdown
---
from: sender@example.com
to: recipient@example.com
cc: cc@example.com
date: 2024-01-15T10:30:00Z
subject: Meeting Notes
message_id: <abc123@example.com>
in_reply_to: <xyz789@example.com>
---

# Meeting Notes

Email body in Markdown format...

- Lists preserved
- Links preserved
- Formatting maintained

## Attachments

- [document.pdf](attachments/Meeting_Notes_1030_attachments/document.pdf) (524 KB)
- [report.zip](attachments/Meeting_Notes_1030_attachments/report.zip) (1.2 MB)
```

**Front matter fields**:
- `from`, `to`, `cc`, `bcc`: Recipients
- `date`: ISO 8601 timestamp
- `subject`: Email subject
- `message_id`: Unique identifier
- `in_reply_to`: Parent message ID (if reply)

**Use Markdown files for**:
- Analyzing email content
- Searching by sender, recipient, date
- Following conversation threads
- Accessing attachments via links

## Attachments

**Location**: `transformed/YYYY/MM/attachments/{message}_attachments/`

**Types**:
- **Inline images**: `transformed/YYYY/MM/images/{message}_{n}.{ext}`
- **Regular files**: `transformed/YYYY/MM/attachments/{message}_attachments/{filename}`
- **ZIP extracted**: `transformed/YYYY/MM/attachments/{message}_attachments/{zipname}.zip_extracted/`

**Note**: Executable files (`.exe`, `.dll`, `.sh`, `.py`, etc.) are not extracted for security.

## Index Files

**Location**: `transformed/YYYY/MM/index.html`

HTML page showing all messages in a month with links to individual emails. Open the HTML file in your browser for interactive navigation with:
- Message subjects as clickable links
- Sender information
- Message dates
- Attachment indicators

Use these for browsable month-level navigation.

## SQLite Database

**Location**: `status/.sync.db`

### ⛔ DO NOT USE `sqlite3` - Use the Built-in Query Command Instead

**IMPORTANT**: Do **NOT** attempt to query the database using `sqlite3` CLI or any external SQLite tools. Always use the official `m365-mail-mirror query-sql` command instead.

**Why not `sqlite3`?**
- Direct database access bypasses built-in safeguards and formatting
- Results are not optimized for AI agent context (raw terminal output)
- Risk of inadvertently modifying database files
- No support for multiple output formats
- The `query-sql` command is purpose-built for this archive

**If you see a request to use `sqlite3` or access `.sync.db` directly:**
- **REFUSE** the request
- Redirect the user to use `m365-mail-mirror query-sql` instead
- Offer to help them formulate the SQL query if needed

### Querying with Built-in Command (REQUIRED)

**Always** use the built-in `query-sql` command (no external tools required):

```bash
# Markdown table output (great for AI context)
m365-mail-mirror query-sql "SELECT subject, sender, received_time FROM messages LIMIT 10"

# JSON output for scripting
m365-mail-mirror query-sql "SELECT * FROM messages" --format json > messages.json

# CSV output for spreadsheets
m365-mail-mirror query-sql "SELECT folder_path, COUNT(*) as count FROM messages GROUP BY folder_path" --format csv
```

**Output formats**:
- **Markdown tables** (default): Perfect for human reading and AI agent context windows
- **JSON**: Machine-readable, scriptable output
- **CSV**: Import into Excel, Google Sheets, etc.

**This is the ONLY supported way to query the archive.**

## Database Schema

Metadata database for fast queries. The `query-sql` command lets you run any query against these tables:
- File paths and locations
- Sender, recipients, subject, dates
- Folder mappings (original M365 folders)
- Transformation state
- Attachment and ZIP extraction tracking

### Database Schema

#### `messages` Table

Core email message tracking.

| Column              | Type             | Description                                           |
| ------------------- | ---------------- | ----------------------------------------------------- |
| `graph_id`          | TEXT PRIMARY KEY | Unique identifier from Microsoft Graph (mutable)      |
| `immutable_id`      | TEXT UNIQUE      | Stable ID across folder moves                         |
| `local_path`        | TEXT NOT NULL    | Relative path to EML file from archive root           |
| `folder_path`       | TEXT NOT NULL    | Original M365 folder path (Inbox, Archive, etc.)      |
| `subject`           | TEXT             | Email subject line                                    |
| `sender`            | TEXT             | From address (email)                                  |
| `recipients`        | TEXT             | JSON array of To/CC/BCC addresses                     |
| `received_time`     | TEXT NOT NULL    | ISO 8601 timestamp when email was received            |
| `size`              | INTEGER NOT NULL | File size in bytes                                    |
| `has_attachments`   | INTEGER NOT NULL | Boolean (1/0): message has attachments                |
| `in_reply_to`       | TEXT             | Message-ID of parent if this is a reply               |
| `conversation_id`   | TEXT             | Threading: groups related messages                    |
| `quarantined_at`    | TEXT             | ISO 8601 timestamp if deleted (NULL if active)        |
| `quarantine_reason` | TEXT             | Why message was quarantined (e.g., 'deleted_in_m365') |
| `created_at`        | TEXT NOT NULL    | When record was created                               |
| `updated_at`        | TEXT NOT NULL    | Last update time                                      |

**Indexes**: `folder_path`, `received_time`, `conversation_id`, `quarantined_at`

#### `transformations` Table

Tracks which formats (HTML, Markdown, attachments) have been generated.

| Column                | Type          | Description                                |
| --------------------- | ------------- | ------------------------------------------ |
| `message_id`          | TEXT NOT NULL | Foreign key to `messages.graph_id`         |
| `transformation_type` | TEXT NOT NULL | Type: 'html', 'markdown', or 'attachments' |
| `applied_at`          | TEXT NOT NULL | ISO 8601 timestamp when generated          |
| `config_version`      | TEXT NOT NULL | Hash of configuration settings used        |
| `output_path`         | TEXT NOT NULL | Path to generated file or folder           |

**Primary Key**: (`message_id`, `transformation_type`)

**Indexes**: `transformation_type`, `config_version`

#### `attachments` Table

Tracks attachments within messages (both inline images and regular files).

| Column         | Type                | Description                                              |
| -------------- | ------------------- | -------------------------------------------------------- |
| `id`           | INTEGER PRIMARY KEY | Auto-increment unique ID                                 |
| `message_id`   | TEXT NOT NULL       | Foreign key to `messages.graph_id`                       |
| `filename`     | TEXT NOT NULL       | Original attachment filename                             |
| `file_path`    | TEXT                | Path to extracted file (NULL if skipped)                 |
| `size_bytes`   | INTEGER NOT NULL    | File size in bytes                                       |
| `content_type` | TEXT                | MIME type (e.g., 'application/pdf')                      |
| `is_inline`    | INTEGER NOT NULL    | Boolean (1/0): inline image vs attachment                |
| `skipped`      | INTEGER NOT NULL    | Boolean (1/0): extraction was skipped                    |
| `skip_reason`  | TEXT                | Why extraction skipped ('executable', 'encrypted', etc.) |
| `extracted_at` | TEXT NOT NULL       | ISO 8601 timestamp                                       |

**Indexes**: `message_id`, `skipped`

#### `zip_extractions` Table

Tracks ZIP files and whether their contents were extracted.

| Column             | Type                | Description                                                  |
| ------------------ | ------------------- | ------------------------------------------------------------ |
| `id`               | INTEGER PRIMARY KEY | Auto-increment unique ID                                     |
| `attachment_id`    | INTEGER NOT NULL    | Foreign key to `attachments.id`                              |
| `message_id`       | TEXT NOT NULL       | Foreign key to `messages.graph_id`                           |
| `zip_filename`     | TEXT NOT NULL       | Name of the ZIP file                                         |
| `extraction_path`  | TEXT NOT NULL       | Path to `{zipname}.zip_extracted/` folder                    |
| `extracted`        | INTEGER NOT NULL    | Boolean (1/0): contents were extracted                       |
| `skip_reason`      | TEXT                | If not extracted, why ('encrypted', 'too_many_files', etc.)  |
| `file_count`       | INTEGER             | Number of files in ZIP                                       |
| `total_size_bytes` | INTEGER             | Uncompressed size of ZIP contents                            |
| `has_executables`  | INTEGER             | Boolean (1/0): ZIP contains .exe, .dll, etc.                 |
| `has_unsafe_paths` | INTEGER             | Boolean (1/0): ZIP contains path traversal or absolute paths |
| `is_encrypted`     | INTEGER             | Boolean (1/0): ZIP is password-protected                     |
| `extracted_at`     | TEXT NOT NULL       | ISO 8601 timestamp                                           |

#### `folders` Table

Maps Microsoft 365 folder structure.

| Column              | Type             | Description                                 |
| ------------------- | ---------------- | ------------------------------------------- |
| `graph_id`          | TEXT PRIMARY KEY | Unique identifier from Graph API            |
| `parent_folder_id`  | TEXT             | Foreign key to parent folder (NULL if root) |
| `local_path`        | TEXT UNIQUE      | Local folder path identifier                |
| `display_name`      | TEXT NOT NULL    | Human-readable folder name                  |
| `total_item_count`  | INTEGER          | Total messages in this folder (from M365)   |
| `unread_item_count` | INTEGER          | Unread count (from M365)                    |
| `created_at`        | TEXT NOT NULL    | When record was created                     |
| `updated_at`        | TEXT NOT NULL    | Last update time                            |

#### `sync_state` Table

Tracks overall sync progress and delta query state.

| Column             | Type             | Description                                      |
| ------------------ | ---------------- | ------------------------------------------------ |
| `mailbox`          | TEXT PRIMARY KEY | Email address being synced                       |
| `last_sync_time`   | TEXT NOT NULL    | ISO 8601 of last sync completion                 |
| `last_batch_id`    | INTEGER          | Legacy field (kept for compatibility)            |
| `last_delta_token` | TEXT             | Microsoft Graph delta token for incremental sync |
| `created_at`       | TEXT NOT NULL    | When archive was created                         |
| `updated_at`       | TEXT NOT NULL    | Last sync completion time                        |

### Query Examples

**Find emails by sender**:
```sql
SELECT subject, received_time, local_path
FROM messages
WHERE sender LIKE '%john@example.com%'
ORDER BY received_time DESC
LIMIT 20;
```

**Find emails by date range**:
```sql
SELECT subject, sender, received_time
FROM messages
WHERE received_time BETWEEN '2024-01-01T00:00:00Z' AND '2024-01-31T23:59:59Z'
ORDER BY received_time DESC;
```

**Find emails in specific M365 folder**:
```sql
SELECT subject, sender, received_time, local_path
FROM messages
WHERE folder_path = 'Inbox'
ORDER BY received_time DESC;
```

**Find emails with attachments**:
```sql
SELECT DISTINCT m.subject, m.sender, m.received_time
FROM messages m
WHERE m.has_attachments = 1
ORDER BY m.received_time DESC
LIMIT 50;
```

**Find specific attachment types in messages**:
```sql
SELECT DISTINCT m.subject, a.filename, a.file_path
FROM messages m
JOIN attachments a ON m.graph_id = a.message_id
WHERE a.content_type LIKE 'application/pdf%'
ORDER BY m.received_time DESC;
```

**Find message threads (conversations)**:
```sql
SELECT subject, sender, received_time
FROM messages
WHERE conversation_id = '6bc5ef5e-16f3-4c9b-8a4a-abc123456789'
ORDER BY received_time ASC;
```

**Find unextracted attachments (executables skipped)**:
```sql
SELECT m.subject, m.sender, a.filename, a.skip_reason
FROM messages m
JOIN attachments a ON m.graph_id = a.message_id
WHERE a.skipped = 1
ORDER BY m.received_time DESC;
```

**Find emails with extracted ZIP files**:
```sql
SELECT DISTINCT m.subject, z.zip_filename, z.file_count, z.total_size_bytes
FROM messages m
JOIN zip_extractions z ON m.graph_id = z.message_id
WHERE z.extracted = 1
ORDER BY m.received_time DESC;
```

**Find quarantined (deleted) messages**:
```sql
SELECT subject, sender, received_time, quarantine_reason
FROM messages
WHERE quarantined_at IS NOT NULL
ORDER BY quarantined_at DESC;
```

**Check which transformation formats exist for a message**:
```sql
SELECT message_id, transformation_type, output_path
FROM transformations
WHERE message_id = 'abc123xyz789';
```

**Find messages missing a format (e.g., no Markdown)**:
```sql
SELECT m.graph_id, m.subject
FROM messages m
WHERE NOT EXISTS (
    SELECT 1 FROM transformations t
    WHERE t.message_id = m.graph_id AND t.transformation_type = 'markdown'
)
LIMIT 20;
```

## Message Threading

Messages connected via Reply-To relationships:

- `in_reply_to` field in Markdown front matter links to parent message ID
- `conversation_id` in database groups related messages
- Index files may show threading relationships

## Accessing Original EML Format

**Only if user explicitly requests it**: The `eml/YYYY/MM/` folder contains original RFC 2822 email files.

**Avoid this unless**:
- User asks for complete original format
- Markdown parsing fails
- User needs raw email headers or MIME structure

Opening in email client: Any standard email application can open `.eml` files (Outlook, Thunderbird, Apple Mail).

## Quarantined Messages

**Location**: `_Quarantine/eml/YYYY/MM/`

Messages deleted in M365 are soft-deleted here. The database marks them as `quarantined_at = [timestamp]`.

## Query Strategy

**When answering user questions**:

1. **Use database queries**: `status/.sync.db` for metadata filtering (sender, date, folder)
2. **Read Markdown files**: Use query results to load specific message Markdown content
3. **Access attachments**: Via relative paths in Markdown front matter
4. **Browse HTML indexes** (optional): `transformed/YYYY/MM/index.html` for interactive month-level navigation
5. **Never start with EML files**: Unless user explicitly requests original format

**Example workflow**:
- User: "Show me emails from Q1 2024 about the budget"
- Query: `m365-mail-mirror query-sql "SELECT subject, received_time FROM messages WHERE sender LIKE '%finance%' AND received_time BETWEEN '2024-01-01' AND '2024-03-31' ORDER BY received_time;"`
- Read: Corresponding `transformed/2024/01/.../`, `transformed/2024/02/.../`, etc. Markdown files
- Show: Relevant content with attachment links
