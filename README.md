# m365-mail-mirror

A command-line tool to archive your Microsoft 365 mailbox to local EML files with optional transformations to HTML, Markdown, and extracted attachments.

## Overview

`m365-mail-mirror` synchronizes email messages from Microsoft 365 mailboxes to your local filesystem. It provides:

- **Archival**: Download and preserve emails in standard EML format (RFC 2822 MIME)
- **Offline access**: Access your email archive without internet connectivity
- **Multiple formats**: Optionally generate browsable HTML, AI-friendly Markdown, or extract attachments
- **Flexibility**: Change output formats without re-downloading from Microsoft 365
- **Incremental sync**: Efficient updates that only download new messages

### Use Cases

- Personal email backup for long-term preservation
- Offline email access when traveling or in restricted environments
- Email archive for AI agents and LLMs to search and analyze
- Compliance and legal hold requirements
- Migration preparation or cloud exit strategy

## Understanding the Storage Model

**EML files are the permanent archive.** Everything else is optional and regenerable.

```
<mail-root>/
├── eml/           # Permanent: Original messages in RFC 2822 MIME format
├── html/          # Optional: Browsable HTML pages (regenerable)
├── markdown/      # Optional: AI-friendly Markdown (regenerable)
└── attachments/   # Optional: Extracted attachment files (regenerable)
```

**Key concept**: EML files are downloaded once from Microsoft 365. All other formats (HTML, Markdown, attachments) are generated locally from the EML files. You can:

- Delete HTML/Markdown/attachments to save space (regenerate later with `transform` command)
- Change transformation settings without re-downloading email
- Add new output formats months or years after initial sync

This design separates **downloading** (network I/O, requires M365 access) from **transforming** (local processing, works offline).

## Installation

### .NET Tool (For Developers)

If you have the .NET SDK installed:

```bash
dotnet tool install --global m365-mail-mirror
```

### Self-Contained Executable (For End Users)

Download the latest release for your platform:

- **Windows**: `m365-mail-mirror-win-x64.exe`
- **macOS (Intel)**: `m365-mail-mirror-osx-x64`
- **macOS (Apple Silicon)**: `m365-mail-mirror-osx-arm64`
- **Linux (x64)**: `m365-mail-mirror-linux-x64`
- **Linux (ARM64)**: `m365-mail-mirror-linux-arm64`

No .NET runtime required. Extract and run.

## Quick Start

### 1. Azure AD App Registration

You must register your own Azure AD application to use this tool.

**Required settings**:

- Application type: **Public client**
- API permissions: **Mail.Read** (Delegated)
- Supported accounts: **Any organizational directory and personal Microsoft accounts**

📖 **Step-by-step guide**: [Microsoft App Registration Documentation](https://learn.microsoft.com/en-us/azure/active-directory/develop/quickstart-register-app)

After registration, note your **Client ID** and **Tenant ID** (use `common` for multi-tenant).

### 2. Configure the Tool

Create a configuration file at `~/.config/m365-mail-mirror/config.yaml`:

```yaml
# Azure AD app registration
client_id: "your-app-client-id-here"
tenant_id: "common"  # or your specific tenant ID

# Where to store the email archive
output: "/path/to/mail/archive"

# Optional: Transformations (enable as needed)
transformations:
  generate_html: true          # Browsable HTML pages
  generate_markdown: false     # AI-friendly Markdown
  extract_attachments: false   # Separate attachment files

# HTML-specific options (if generate_html: true)
html:
  inline_styles: false                 # Embed CSS in each HTML file vs external stylesheet
  strip_external_images: false         # Remove external image references
  hide_cc: false                       # Omit CC from email headers
  hide_bcc: false                      # Omit BCC from email headers

# Attachment extraction options (if extract_attachments: true)
attachment_extraction:
  skip_executables: true               # Don't extract .exe, .dll, .sh, etc.

zip_extraction:
  enabled: true                        # Auto-extract ZIP file contents
  min_files: 1                         # Skip empty ZIPs
  max_files: 100                       # Skip huge archives
  skip_encrypted: true                 # Skip password-protected ZIPs
  skip_with_executables: true          # Skip ZIPs containing executables
```

### 3. Authenticate

```bash
m365-mail-mirror auth login
```

You'll receive a device code and URL:

```
To sign in, use a web browser to open the page https://microsoft.com/devicelogin
and enter the code A1B2C3D4 to authenticate.
```

Visit the URL in any browser (on any device), enter the code, and complete authentication.

### 4. Sync Your Mailbox

```bash
m365-mail-mirror sync
```

This will:

1. Download all messages as EML files to `output/eml/`
2. Organize by folder and date: `eml/{folder}/{YYYY}/{MM}/{message}.eml`
3. Generate configured transformations (HTML, Markdown, attachments)
4. Show progress and summary

**First sync may take hours for large mailboxes.** The sync is batched and resumable if interrupted.

### 5. Browse Your Archive

- **EML files**: Open in any email client (Outlook, Thunderbird, Apple Mail)
- **HTML files** (if enabled): Open `html/` folder in web browser, navigate by folder/date
- **Markdown files** (if enabled): Read with any text editor or feed to AI agents
- **Attachments** (if enabled): Access extracted files in `attachments/` folder

### 6. Understanding Attachment Handling

**Executable files**: For security, executable files (`.exe`, `.dll`, `.sh`, `.bat`, etc.) are not extracted by default. Instead, a `.skipped` placeholder file is created explaining why the file was skipped. You can always access the original attachment by opening the EML file in an email client.

**ZIP file extraction**: ZIP attachments are automatically extracted when:

- ZIP extraction is enabled in config
- The ZIP contains 1-100 files (configurable range)
- The ZIP is not password-protected
- The ZIP doesn't contain executable files (if configured to skip)
- All file paths in the ZIP are relative (no absolute paths or path traversal)

When a ZIP is extracted:

- Original ZIP file is preserved
- Contents are extracted to a `{filename}.zip_extracted/` folder
- Both the ZIP and extracted contents are linked in HTML/Markdown views
- Extraction decisions are logged for transparency

**Skipped ZIPs**: ZIPs that don't meet extraction criteria are still saved as attachments, but not auto-extracted. Common reasons include:

- Encrypted/password-protected
- Too many files (> max_files threshold)
- Contains executable files
- Contains unsafe file paths
- Empty or too few files

## Commands

### `sync` - Download and Synchronize Email

Download new messages from Microsoft 365 to local EML files.

```bash
# Standard sync (downloads new messages)
m365-mail-mirror sync

# Dry run (show what would be synced without downloading)
m365-mail-mirror sync --dry-run

# Sync specific mailbox (if you have delegated access)
m365-mail-mirror sync --mailbox shared@example.com

# Customize batch size and parallelism
m365-mail-mirror sync --batch-size 50 --parallel 10
```

**How it works**:

- **Initial sync**: Downloads all messages in batches (default 100 per batch)
- **Incremental sync**: Only downloads messages newer than last sync (with configurable overlap)
- **Resumable**: If interrupted, resumes from last completed batch
- **Checkpointed**: Progress saved after each batch

**Options**:

- `--batch-size <n>`: Messages per batch (default: 100)
- `--parallel <n>`: Concurrent downloads (default: 5)
- `--exclude <pattern>`: Skip folders matching glob pattern (can specify multiple)
- `--overlap <minutes>`: Re-check messages from N minutes before last sync (default: 60)
- `--dry-run`: Show what would be synced without downloading

### `transform` - Regenerate Outputs from EML Files

Generate or regenerate HTML, Markdown, or attachments from existing EML files.

```bash
# Regenerate all enabled transformations
m365-mail-mirror transform

# Regenerate only HTML
m365-mail-mirror transform --only html

# Force regeneration (even if up-to-date)
m365-mail-mirror transform --force

# Dry run (show what would be regenerated)
m365-mail-mirror transform --dry-run
```

**When to use**:

- After changing transformation config (enabled HTML, changed CSS settings)
- After editing HTML templates or Markdown formatting
- To fix corrupted transformation outputs
- To add new transformation types to existing archive

**No internet required**: Works entirely offline from local EML files.

### `status` - Show Archive Status

Display sync status and archive statistics.

```bash
m365-mail-mirror status

# Show quarantine contents
m365-mail-mirror status --quarantine
```

**Example output**:

```
Mailbox: user@example.com
Last sync: 2024-01-19 10:30:00
Messages: 12,450 (8.5 GB)
Folders: 25
Quarantine: 15 messages (2.3 MB)

Transformations:
  HTML: 12,450 messages
  Markdown: 0 messages (disabled)
  Attachments: 0 messages (disabled)
```

### `verify` - Check Archive Integrity

Scan archive for integrity issues.

```bash
# Check integrity
m365-mail-mirror verify

# Check and auto-fix safe issues
m365-mail-mirror verify --fix
```

Checks for:

- Missing EML files referenced in database
- Orphaned database entries (no corresponding EML file)
- EML files not tracked in database
- Incomplete or corrupted transformations

### `auth` - Authentication Management

Manage Microsoft 365 authentication.

```bash
# Interactive login
m365-mail-mirror auth login

# Show authentication status
m365-mail-mirror auth status

# Force token refresh
m365-mail-mirror auth refresh

# Logout (remove stored credentials)
m365-mail-mirror auth logout
```

## Configuration

### Configuration File Location

Default: `~/.config/m365-mail-mirror/config.yaml`

Override with `--config <path>` flag.

### Full Configuration Example

```yaml
# Azure AD app registration
client_id: "12345678-1234-1234-1234-123456789012"
tenant_id: "common"

# Sync settings
output: "/Users/username/mail-archive"
mailbox: "user@example.com"  # Optional: defaults to authenticated user
batch_size: 100
parallel: 5
overlap_minutes: 60

# Folder filtering (glob patterns)
exclude:
  - "Junk Email"
  - "Deleted Items"
  - "Archive/Old Projects/*"
  - "RSS Subscriptions"

# Transformations
transformations:
  generate_html: true
  generate_markdown: true
  extract_attachments: true

# HTML-specific options
html:
  inline_styles: false          # Use external stylesheet (recommended)
  strip_external_images: false  # Keep external images (may not load offline)
  hide_cc: false                # Show CC recipients
  hide_bcc: false               # Show BCC recipients

# Markdown-specific options (future)
markdown:
  include_headers: true
  format_style: "gfm"  # GitHub-flavored Markdown

# Attachment extraction options
attachment_extraction:
  skip_executables: true        # Don't extract executable files (.exe, .dll, .sh, etc.)

# ZIP file extraction
zip_extraction:
  enabled: true                 # Auto-extract ZIP contents
  min_files: 1                  # Minimum files to extract (skip empty ZIPs)
  max_files: 100                # Maximum files to extract (skip huge archives)
  skip_encrypted: true          # Don't extract password-protected ZIPs
  skip_with_executables: true   # Don't extract ZIPs containing executables
```

### Configuration Precedence

1. **Command-line flags** (highest priority)
2. **Environment variables** (`M365_MIRROR_*` prefix)
3. **Configuration file** (lowest priority)

Example:

```bash
# Command-line flag overrides config file
m365-mail-mirror sync --batch-size 50

# Environment variable
export M365_MIRROR_BATCH_SIZE=50
m365-mail-mirror sync
```

For detailed documentation, see:

- **Technical design**: [DESIGN.md](DESIGN.md)
- **Architecture decisions**: [decisions/](decisions/)

## Getting Help

- **Issues**: Report bugs or request features on GitHub Issues
- **CLI help**: Run `m365-mail-mirror --help` or `m365-mail-mirror <command> --help`

## License

[To be determined]
