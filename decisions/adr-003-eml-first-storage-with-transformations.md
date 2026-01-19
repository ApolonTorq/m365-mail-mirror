# ADR-003: EML-First Storage with Configurable Transformations

## Status

Accepted

## Context

The application needs to archive email messages from Microsoft 365 mailboxes for multiple purposes:

- Long-term preservation
- Offline access
- Human browsing and reading
- AI/LLM processing and analysis
- Attachment extraction and management

Different use cases require different output formats:

- **Human users**: Need browsable HTML with email-client-style formatting
- **AI agents**: Need clean Markdown or structured text for context understanding
- **File management**: Need extracted attachments as separate files
- **Archival**: Need full-fidelity preservation of original messages

The tool must balance:

- **Fidelity**: Preserve complete message data (headers, body, attachments, metadata)
- **Flexibility**: Support multiple output formats without re-downloading
- **Efficiency**: Avoid storing redundant data
- **Usability**: Provide format appropriate for each use case

## Decision

Store email messages as EML files (RFC 2822 MIME format) as the canonical archive format. Generate HTML, Markdown, and extracted attachments as optional, regenerable transformations configurable via YAML settings.

### Key Principles

- **EML files are canonical**: The permanent source of truth
- **Transformations are derived**: Generated from EML files and regenerable at any time
- **User controls output formats**: Configuration determines which transformations are enabled
- **Offline regeneration**: Transformations can be regenerated without network access or API calls

## Rationale

### Why EML as Canonical Format

**Complete fidelity**: EML (RFC 2822 MIME) is the native wire format for email. Microsoft Graph API provides MIME content directly, preserving all RFC 2822 headers, full MIME structure, inline content, file attachments, transport headers, and Microsoft-specific headers.

**Industry standard**: EML is universally recognized and supported by all email clients, email forensics tools, archive utilities, and MIME parsing libraries in every language.

**Future-proof**: As a standardized format, EML files remain accessible regardless of changes to this tool's implementation, output format preferences, or future transformation requirements.

### Why Transformations Are Derived

**Regenerable**: Transformations can always be recreated from EML files. Users can delete transformed outputs to save space, change transformation settings without re-downloading, regenerate corrupted transformations, and add new transformation formats without touching the archive.

**No data loss**: Deleting HTML, Markdown, or extracted attachments never risks losing email data. The canonical EML always preserves the original message.

**Experimentation-friendly**: Users can try different HTML styling without re-syncing, enable Markdown generation months after initial sync, change attachment extraction settings, and A/B test different output formats.

### Why Multiple Output Formats

**Different tools, different needs**:

- **HTML**: Browsing in web browser, email-client visual presentation, search indexing
- **Markdown**: LLM context, plaintext search, version control, documentation tools
- **Attachments**: File management, opening documents directly, backup verification

**Use case alignment**: Personal users typically want HTML for browsing, developers may want Markdown for search, AI agents benefit from structured Markdown, and compliance teams may need extracted attachments for records management.

**Configurable**: Not all users need all formats. Configuration allows minimal storage (EML only), human-focused (EML + HTML), AI-focused (EML + Markdown), or full suite (EML + all transformations).

## Consequences

### Positive

- **Maximum flexibility**: Change output formats without re-downloading from M365
- **Storage efficiency**: Only store canonical EML, generate transformations as needed
- **Future-proof**: Can add new transformation formats (PDF, mbox, etc.) without touching archive
- **Fast iteration**: Experiment with HTML templates, Markdown formats, etc. locally
- **Standard format**: EML files work with existing email tools
- **No vendor lock-in**: Archive is portable, not dependent on this tool
- **Reduced API usage**: Transformations use local EML files, no M365 API calls

### Negative

- **Additional storage**: Enabling all transformations increases disk usage
- **Processing time**: Initial sync takes longer when transformations are enabled
- **Complexity**: More moving parts (EML parser, HTML generator, Markdown generator)
- **Two-step workflow**: Users wanting format changes must run `transform` command

### Neutral

- **EML parsing dependency**: Requires robust MIME parsing library
- **Transformation quality**: HTML/Markdown output quality depends on parser and generator implementations
- **Configuration surface**: More options to understand (transformation flags, format-specific settings)

## Alternatives Considered

### Direct HTML Storage

**Approach**: Download messages and immediately convert to HTML, storing only HTML files.

**Rejected because**: No format flexibility (changing templates requires re-downloading), fidelity loss (HTML conversion may lose MIME details), limited use cases (only supports browsing, not AI processing), and attachment handling requires parallel storage structure anyway.

### Proprietary Database Format

**Approach**: Store messages in SQLite or custom database with indexed search.

**Rejected because**: Vendor lock-in (requires this tool or schema knowledge to access emails), migration complexity (moving to different tools requires export), corruption risk (database corruption affects all messages), and not human-readable (can't inspect individual messages easily).

### Multiple Canonical Formats

**Approach**: Store both EML and HTML as equally-canonical formats.

**Rejected because**: Redundant storage (wastes disk space), sync complexity (must keep both in sync), no clear source of truth (which format is authoritative?), and conflicts (what if EML and HTML diverge?).

### Transformation on Demand

**Approach**: Only generate transformations when accessed (e.g., via HTTP server).

**Rejected because**: Latency (users wait for transformation on first access), complexity (requires caching layer), offline use (doesn't work without server running), and batch operations (can't pre-generate all outputs for offline browsing).

## References

- RFC 2822: Internet Message Format: https://datatracker.ietf.org/doc/html/rfc2822
- RFC 2045-2049: MIME (Multipurpose Internet Mail Extensions): https://datatracker.ietf.org/doc/html/rfc2045
- Microsoft Graph Get Message MIME: https://learn.microsoft.com/en-us/graph/api/message-get#example-2-get-mime-content
- See [DESIGN.md](../DESIGN.md) for implementation details (pipeline, HTML/Markdown generation, attachment extraction)
