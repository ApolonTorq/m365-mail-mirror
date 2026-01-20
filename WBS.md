# Work Breakdown Structure (WBS)

## Overview

This document tracks the implementation status of features for m365-mail-mirror across versions. Each feature corresponds to architectural decisions documented in ADRs and technical specifications in DESIGN.md.

**Purpose**:

- Track what features are implemented vs planned
- Guide implementation priorities
- Provide visibility into project progress

**Usage**:

- Check boxes are marked when features are fully implemented and tested
- Testing is implicit - each feature includes its own test coverage
- Features are organized by functional area and version

## Version 1.0 - Core Features

### Project Infrastructure

- [x] .NET 10 solution and project structure with CliFx CLI framework
- [x] NuGet package dependencies (Microsoft.Graph, MimeKit, SQLite, YamlDotNet, MSAL)
- [x] Build configuration and CI/CD pipeline (GitHub Actions, multi-platform)
- [x] Configuration system (YAML file parsing, environment variables, command-line overrides)
- [x] Logging infrastructure (structured logging with severity levels)

### Authentication (ADR-002)

- [x] OAuth device code flow implementation with Microsoft Identity Platform
- [x] Platform-specific token storage (Windows Credential Manager, macOS Keychain, Linux Secret Service)
- [x] Automatic token refresh before expiration
- [x] Auth command (login, logout, status)

### Microsoft Graph API Integration (ADR-001)

- [x] Graph API client configuration with proper scopes (Mail.ReadWrite, offline_access)
- [x] Mailbox and folder enumeration (recursive subfolder support)
- [x] MIME message download (preserving RFC 2822 format)
- [x] Delta query support for incremental sync with delta token persistence
- [x] Rate limiting and error handling (retries, throttling)

### Storage Architecture (ADR-003, ADR-004)

- [x] EML file storage system with folder/date hierarchy
- [x] File naming algorithm (sanitization, HHMM suffix, collision handling)
- [x] SQLite database schema (8 tables: messages, sync_state, transformations, attachments, etc.)
- [x] Database operations (message indexing, folder tracking, transformation state)
- [x] Atomic file writes and transaction safety

### Sync Engine (ADR-001)

- [x] Initial sync with batch processing (100 messages/batch, checkpointing, resumption)
- [x] Incremental sync using delta queries with date-based fallback
- [x] Folder enumeration, mapping (Graph ID to local path), and change detection
- [x] Folder move handling (file relocation, database updates)
- [x] Sync command with dry-run mode, folder exclusion, and parallelization

### Quarantine System (ADR-005)

- [x] Deletion detection from delta queries
- [x] File movement to _Quarantine/ (preserving folder structure)
- [x] Database quarantine tracking (quarantined_at, quarantine_reason)
- [x] Quarantine management via status and verify commands

### Transformation Pipeline (ADR-003, ADR-006)

- [x] HTML transformation (MimeKit → HTML with CSS styling, thread navigation, XSS filtering)
- [x] Markdown transformation (YAML front matter, HTML-to-Markdown conversion, LLM-optimized)
- [x] Attachment extraction (filename preservation, conflict resolution, inline detection)
- [x] Transform command (selective regeneration, config version detection, force mode)
- [x] Transformation state tracking (config versioning, regeneration triggers)

### Security Features (ADR-007)

- [x] Executable file filtering (42-extension blocklist, .skipped placeholder files)
- [x] ZIP extraction with safety checks (decision tree: size limits, encryption, path validation)
- [x] Path safety validation (absolute paths, traversal prevention, UNC blocking)
- [x] Security logging (extraction decisions, skipped files, threat detection)

### Utility Commands

- [x] Status command (archive statistics, sync progress, quarantine contents)
- [x] Verify command (EML integrity, database consistency, orphaned files, auto-fix)

### Distribution

- [x] .NET Global Tool packaging (dotnet tool install)
- [x] Self-contained executables for all platforms (Windows x64, macOS Intel/ARM, Linux x64/ARM64)
- [x] Installation documentation and quick start guide
