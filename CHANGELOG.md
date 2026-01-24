<!-- markdownlint-disable-file MD024 -->
# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Feature completion is tracked with checkboxes:

- `[x]` = Fully implemented and tested
- `[ ]` = Planned or in progress

## [Unreleased]

### Added

- [x] **Glob pattern support for folder exclusions**: The `excludeFolders` config and `--exclude` CLI option now support glob patterns: `*` for single-segment wildcards (e.g., `Inbox/Azure*`), `/*` for immediate children only (e.g., `Robots/*`), `/**` for all descendants (e.g., `Archive/**`), and `**/` prefix for matching at any depth (e.g., `**/Old*`). Matching is case-insensitive.
- [x] **Index files and breadcrumb navigation**: Generates `index.html` and `index.md` files at each folder level (root, mail folders, years, months) for archive navigation. All pages (index files and individual emails) include clickable breadcrumb navigation showing the path hierarchy (Archive > Folder > Year > Month > Subject). Index generation runs automatically after sync and transform commands complete.
- [x] **Streaming sync with checkpointing** (ADR-008): Per-message checkpointing via `--checkpoint-interval`, `FolderSyncProgress` entity, database schema v2 with `folder_sync_progress` table
- [x] **AAD throttling mitigation** (ADR-009): Token caching with 5-min proactive refresh, exponential backoff retries, local-only cache reads for `GetStatusAsync`
- [x] **Inline transformation during sync**: `--html`, `--markdown`, `--attachments` flags work during sync via `TransformSingleMessageAsync`
- [x] **Integration tests**: Test fixture with auth/config loading, console capture utilities, coverage for sync/transform/status/verify commands
- [x] **CLI improvements**: Verbose flag (`-v`), configurable log output writers, improved progress reporting
- [x] **Attachment links in HTML/Markdown output**: Generated HTML and Markdown files now include clickable links to extracted attachments with relative paths and human-readable file sizes
- [x] **CC/BCC fields in output**: HTML and Markdown transformations now include CC and BCC recipients when present
- [x] **Configuration-first transformation options** (ADR-010): Transformation settings (HTML, Markdown, attachments) now default from config file with CLI flags as overrides. New options: `inlineStyles`, `stripExternalImages`, `hideCc`, `hideBcc` for HTML; `skipExecutables` for attachments
- [x] **Outlook Web deep links** (ADR-011): HTML and Markdown outputs include optional "View in Outlook" link using ImmutableId for stable navigation back to source message. Supports both personal and shared mailboxes via `includeOutlookLink` config option

### Changed

- [x] Renamed `--batch-size` to `--checkpoint-interval` (default 10)
- [x] Database uses private cache mode and disabled pooling for better concurrency
- [x] Folder upsert handles mutable→immutable Graph ID migration (preserves delta tokens)
- [x] Transformation ordering: Attachments are now extracted before HTML/Markdown generation to ensure attachment links are available
- [x] Attachment re-extraction: Running transform with `--attachments --force` now deletes existing attachment records and files before re-extracting
- [x] Transformation config version bumped to v4 (for Outlook link support); run `transform --force` to regenerate existing outputs

### Fixed

- [x] Thread-safe database disposal with proper locking to prevent race conditions during async cleanup
- [x] Index file month links now use numeric folder names (e.g., `01/index.html`) instead of month names (`January/index.html`) to match actual directory structure

## [0.1.0] - Initial Release

Initial version where code was implemented by Claude Code based on the [README](README.md) and [DESIGN](DESIGN.md) documentation.

### Added

#### Project Infrastructure

- [x] .NET 10 solution and project structure with CliFx CLI framework
- [x] NuGet package dependencies (Microsoft.Graph, MimeKit, SQLite, YamlDotNet, MSAL)
- [x] Build configuration and CI/CD pipeline (GitHub Actions, multi-platform)
- [x] Configuration system (YAML file parsing, environment variables, command-line overrides)
- [x] Logging infrastructure (structured logging with severity levels)

#### Authentication (ADR-002)

- [x] OAuth device code flow implementation with Microsoft Identity Platform
- [x] Platform-specific token storage (Windows Credential Manager, macOS Keychain, Linux Secret Service)
- [x] Automatic token refresh before expiration
- [x] Auth command (login, logout, status)

#### Microsoft Graph API Integration (ADR-001)

- [x] Graph API client configuration with proper scopes (Mail.ReadWrite, offline_access)
- [x] Mailbox and folder enumeration (recursive subfolder support)
- [x] MIME message download (preserving RFC 2822 format)
- [x] Delta query support for incremental sync with delta token persistence
- [x] Rate limiting and error handling (retries, throttling)

#### Storage Architecture (ADR-003, ADR-004)

- [x] EML file storage system with folder/date hierarchy
- [x] File naming algorithm (sanitization, HHMM suffix, collision handling)
- [x] SQLite database schema (8 tables: messages, sync_state, transformations, attachments, etc.)
- [x] Database operations (message indexing, folder tracking, transformation state)
- [x] Atomic file writes and transaction safety

#### Sync Engine (ADR-001)

- [x] Initial sync with batch processing (100 messages/batch, checkpointing, resumption)
- [x] Incremental sync using delta queries with date-based fallback
- [x] Folder enumeration, mapping (Graph ID to local path), and change detection
- [x] Folder move handling (file relocation, database updates)
- [x] Sync command with dry-run mode, folder exclusion, and parallelization

#### Quarantine System (ADR-005)

- [x] Deletion detection from delta queries
- [x] File movement to _Quarantine/ (preserving folder structure)
- [x] Database quarantine tracking (quarantined_at, quarantine_reason)
- [x] Quarantine management via status and verify commands

#### Transformation Pipeline (ADR-003, ADR-006)

- [x] HTML transformation (MimeKit → HTML with CSS styling, thread navigation, XSS filtering)
- [x] Markdown transformation (YAML front matter, HTML-to-Markdown conversion, LLM-optimized)
- [x] Attachment extraction (filename preservation, conflict resolution, inline detection)
- [x] Transform command (selective regeneration, config version detection, force mode)
- [x] Transformation state tracking (config versioning, regeneration triggers)

#### Security Features (ADR-007)

- [x] Executable file filtering (42-extension blocklist, .skipped placeholder files)
- [x] ZIP extraction with safety checks (decision tree: size limits, encryption, path validation)
- [x] Path safety validation (absolute paths, traversal prevention, UNC blocking)
- [x] Security logging (extraction decisions, skipped files, threat detection)

#### Utility Commands

- [x] Status command (archive statistics, sync progress, quarantine contents)
- [x] Verify command (EML integrity, database consistency, orphaned files, auto-fix)

#### Distribution

- [x] .NET Global Tool packaging (dotnet tool install)
- [x] Self-contained executables for all platforms (Windows x64, macOS Intel/ARM, Linux x64/ARM64)
- [x] Installation documentation and quick start guide

[Unreleased]: https://github.com/torq-lang/m365-mail-mirror/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/torq-lang/m365-mail-mirror/releases/tag/v0.1.0
