# ADR-007: ZIP File Extraction and Executable Filtering

## Status

Accepted

## Context

Email attachments often include compressed archive files (ZIP is most common) and executable files. Users want convenient access to archive contents without manual extraction, but this creates security risks if malicious or unwanted files are automatically extracted.

### Use Cases

**ZIP files in email**:

- Reports and documentation bundles (multiple related files)
- Backup archives and data exports
- Code repositories or project exports
- Photo collections or image galleries

**Security concerns**:

- Executable files may contain malware
- ZIP files can contain malicious payloads disguised as documents
- Path traversal attacks (e.g., `../../../etc/passwd`)
- Encrypted ZIPs may hide malicious content
- Large archives can cause filesystem stress

**Operational concerns**:

- Not all ZIPs should be extracted (password-protected, huge archives, executables)
- Users need visibility into extraction decisions
- Extraction must be safe by default
- Original ZIP should be preserved for manual extraction if needed

## Decision

Implement automatic ZIP file extraction with strict safety checks, while blocking executable file attachments by default. Make extraction configurable and provide transparency through logging and placeholder files.

### Core Principles

1. **ZIP extraction is opt-in via configuration** (can be disabled entirely)
2. **Original ZIP is always preserved** (extraction creates additional folder)
3. **Security checks before extraction** (scan for unsafe paths, executables, encryption)
4. **Size-based filtering** (skip empty ZIPs and huge archives via configurable thresholds)
5. **Executable files are blocked by default** (both as direct attachments and within ZIPs)
6. **Transparent logging** (every extraction decision is recorded)
7. **Placeholder files for skipped content** (make absence visible with explanation)

### Configuration Approach

Users control extraction behavior through YAML settings covering:

- Whether to enable ZIP extraction at all
- Minimum and maximum file count thresholds
- Whether to skip encrypted ZIPs
- Whether to skip ZIPs containing executables
- Whether to block executable file extraction

## Rationale

### Why Auto-Extract ZIP Files

**User convenience**: Most ZIP attachments in email contain related documents that users want to access directly (multi-file reports, documentation bundles, photo collections). Requiring manual extraction adds friction.

**Indexed access**: Extracted files can be linked from HTML/Markdown transformations, indexed by filesystem search tools, previewed in file browsers, and accessed by automation.

**Preservation of user intent**: ZIP files attached to email typically represent "here are multiple related files" rather than "here is an archive to be kept compressed."

### Why Preserve Original ZIP

**Manual extraction option**: If auto-extraction is skipped or fails, users can extract manually with their preferred tool, use passwords for encrypted ZIPs, or apply custom extraction settings.

**Audit trail**: Original ZIP serves as proof of what was received, backup if extraction becomes corrupted, and reference for file metadata.

**Disk space trade-off**: Storing both ZIP and extracted contents uses more space, but most email ZIPs are small and disk space is cheap relative to user time. Users can delete extracted folders if space constrained.

### Why Block Executable Files

**Security risk**: Executable files in email attachments are the primary malware distribution vector, often disguised with double extensions, unsafe to extract automatically, and rarely legitimate in personal/business email.

**Defense in depth**: Blocking executables provides protection even if antivirus misses malware, users accidentally double-click, or filesystem permissions are misconfigured.

**User expectation**: Most users do not expect email archive tools to extract executables. For rare legitimate cases, users can open EML in email client, extract manually from ZIP, or temporarily disable the filtering.

### Why Size-Based Filtering

**Empty ZIPs**: ZIP with 0-1 files is likely a mistake, corrupt, or not worth extracting (single file can be accessed from ZIP directly).

**Huge ZIPs**: ZIP with hundreds/thousands of files is likely a backup archive or data dump (not casual document bundle), expensive to extract, better left compressed, and user probably expects it to remain zipped.

Configurable thresholds balance convenience with safety.

### Why Skip Encrypted ZIPs

**No password available**: Application has no way to know the password without user interaction, making batch processing impossible.

**Security indicator**: Password protection often indicates sensitive content, intentional access control, or potentially malicious content (attackers use passwords to evade scanning).

**Manual extraction preferred**: User can extract manually with password when needed.

### Why Path Safety Checks

**Absolute paths and path traversal**: Malicious ZIPs can attempt to write outside the extraction folder using absolute paths or `..` navigation, potentially overwriting system files, planting malware in startup folders, or modifying user configuration.

**Mitigation**: Scan ZIP entries before extraction and reject any with absolute paths or path traversal segments.

### Why Placeholder Files for Skipped Content

**User visibility**: Without placeholders, skipped attachments are invisible in file browsers with no explanation. Placeholder files make absence visible, explain why extraction was skipped, provide instructions for manual extraction, and preserve attachment metadata.

**Audit trail**: Logs record decisions but may be rotated or require searching. Placeholder files persist alongside messages, making decisions immediately visible.

## Consequences

### Positive

- **Convenience**: Users access ZIP contents directly without manual extraction
- **Safety**: Executable files blocked, path traversal prevented
- **Transparency**: All extraction decisions logged and visible via placeholder files
- **Flexibility**: Original ZIP preserved for manual extraction when needed
- **Configurability**: Users can tune extraction thresholds and disable features

### Negative

- **Storage overhead**: Storing both ZIP and extracted contents uses 2x space for ZIP file size
- **Processing time**: ZIP scanning and extraction adds time to operations
- **Complexity**: More configuration options to understand and tune
- **False positives**: Legitimate executables (software distributions) blocked by default
- **ZIP-only**: 7z, RAR, TAR.GZ not supported (must be extracted manually)

### Neutral

- **File count thresholds**: Defaults may not suit all use cases (but configurable)
- **Executable definition**: Blocked extension list may need updates for new file types
- **Encryption handling**: Cannot extract password-protected ZIPs (acceptable limitation)
- **Platform differences**: Some executable extensions only relevant on specific OSes

## Alternatives Considered

### Never Extract ZIPs

**Approach**: Always save ZIP as single file, never auto-extract.

**Rejected because**: Reduces convenience, misses value-add opportunity (most ZIPs are safe document bundles), and original spec requested ZIP extraction feature. Configurable `enabled: false` provides this behavior for users who want it.

### Extract All ZIPs Unconditionally

**Approach**: Extract every ZIP, no safety checks or size limits.

**Rejected because**: Security risk (malicious ZIPs, path traversal), performance risk (huge archives), storage risk (encrypted ZIPs, backups), and user expectation mismatch (some ZIPs should stay zipped).

### Extract Executables But Quarantine

**Approach**: Extract executables to separate folder or mark with special permissions.

**Rejected because**: Still creates security risk (user may accidentally execute), complex to implement across platforms (permissions differ), and better to be conservative (skip by default, user can override). Executable email attachments are rarely legitimate.

### Support 7z, RAR, TAR.GZ

**Approach**: Implement extraction for multiple archive formats.

**Rejected because**: ZIP is dominant format for email attachments (>90%), additional dependencies needed (7z libraries, RAR licenses), complexity for marginal benefit, and user can extract other formats manually. May add in future if demand exists.

### Delete Original ZIP After Extraction

**Approach**: Only keep extracted contents, remove ZIP file.

**Rejected because**: Prevents manual extraction with different tools/settings, loses original file metadata, user may prefer to keep ZIP for space savings, and goes against "EML-first, everything regenerable" principle.

## References

- ZIP file format specification: https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT
- Path traversal vulnerability: https://owasp.org/www-community/attacks/Path_Traversal
- See [DESIGN.md](../DESIGN.md) for implementation details (schemas, algorithms, directory structures)
