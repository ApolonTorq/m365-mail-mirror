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

- [ ] .NET 10 solution and project structure with CliFx CLI framework
- [ ] NuGet package dependencies (Microsoft.Graph, MimeKit, SQLite, YamlDotNet, MSAL)
- [ ] Build configuration and CI/CD pipeline (GitHub Actions, multi-platform)
- [ ] Configuration system (YAML file parsing, environment variables, command-line overrides)
- [ ] Logging infrastructure (structured logging with severity levels)

### Authentication (ADR-002)

- [ ] OAuth device code flow implementation with Microsoft Identity Platform
- [ ] Platform-specific token storage (Windows Credential Manager, macOS Keychain, Linux Secret Service)
- [ ] Automatic token refresh before expiration
- [ ] Auth command (login, logout, status)

### Microsoft Graph API Integration (ADR-001)

- [ ] Graph API client configuration with proper scopes (Mail.ReadWrite, offline_access)
- [ ] Mailbox and folder enumeration (recursive subfolder support)
- [ ] MIME message download (preserving RFC 2822 format)
- [ ] Delta query support for incremental sync with delta token persistence
- [ ] Rate limiting and error handling (retries, throttling)

### Storage Architecture (ADR-003, ADR-004)

- [ ] EML file storage system with folder/date hierarchy
- [ ] File naming algorithm (sanitization, HHMM suffix, collision handling)
- [ ] SQLite database schema (8 tables: messages, sync_state, transformations, attachments, etc.)
- [ ] Database operations (message indexing, folder tracking, transformation state)
- [ ] Atomic file writes and transaction safety

### Sync Engine (ADR-001)

- [ ] Initial sync with batch processing (100 messages/batch, checkpointing, resumption)
- [ ] Incremental sync using delta queries with date-based fallback
- [ ] Folder enumeration, mapping (Graph ID to local path), and change detection
- [ ] Folder move handling (file relocation, database updates)
- [ ] Sync command with dry-run mode, folder exclusion, and parallelization

### Quarantine System (ADR-005)

- [ ] Deletion detection from delta queries
- [ ] File movement to _Quarantine/ (preserving folder structure)
- [ ] Database quarantine tracking (quarantined_at, quarantine_reason)
- [ ] Quarantine management via status and verify commands

### Transformation Pipeline (ADR-003, ADR-006)

- [ ] HTML transformation (MimeKit → HTML with CSS styling, thread navigation, XSS filtering)
- [ ] Markdown transformation (YAML front matter, HTML-to-Markdown conversion, LLM-optimized)
- [ ] Attachment extraction (filename preservation, conflict resolution, inline detection)
- [ ] Transform command (selective regeneration, config version detection, force mode)
- [ ] Transformation state tracking (config versioning, regeneration triggers)

### Security Features (ADR-007)

- [ ] Executable file filtering (42-extension blocklist, .skipped placeholder files)
- [ ] ZIP extraction with safety checks (decision tree: size limits, encryption, path validation)
- [ ] Path safety validation (absolute paths, traversal prevention, UNC blocking)
- [ ] Security logging (extraction decisions, skipped files, threat detection)

### Utility Commands

- [ ] Status command (archive statistics, sync progress, quarantine contents)
- [ ] Verify command (EML integrity, database consistency, orphaned files, auto-fix)

### Distribution

- [ ] .NET Global Tool packaging (dotnet tool install)
- [ ] Self-contained executables for all platforms (Windows x64, macOS Intel/ARM, Linux x64/ARM64)
- [ ] Installation documentation and quick start guide
