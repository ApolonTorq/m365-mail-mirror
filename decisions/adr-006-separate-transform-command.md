# ADR-006: Separate Transform Command

## Status

Accepted

## Context

With EML files as the canonical archive format (see [ADR-003](adr-003-eml-first-storage-with-transformations.md)), users may want to:

- Change output formats after initial sync (enable HTML when it was disabled)
- Modify transformation settings (switch from external to inline CSS)
- Add new transformation types (enable Markdown months later)
- Regenerate corrupted transformations
- Experiment with different HTML templates or Markdown formats

Since EML files are already downloaded, these operations should not require re-downloading messages from Microsoft 365, internet connectivity, Microsoft Graph API calls, or additional API rate limit consumption.

The tool must provide a mechanism to regenerate transformed outputs from existing EML files. Design options include:

1. **Auto-detect in sync command**: `sync` detects config changes and regenerates
2. **Separate transform command**: Explicit `transform` command for regeneration
3. **Flag-based**: `sync --regenerate` triggers transformation updates
4. **Always regenerate**: Every sync regenerates all transformations

## Decision

Provide a separate `transform` command that regenerates outputs from existing EML files based on current configuration. The `sync` command focuses solely on downloading new messages from M365.

## Rationale

### Why Separate Command

**Clear separation of concerns**:

- **`sync`**: Network I/O, API interaction, downloading messages
- **`transform`**: Local processing, MIME parsing, generating outputs

Each command has a single, well-defined responsibility.

**Explicit user intent**: When users run `transform`, they explicitly request regeneration. No ambiguity about whether sync will touch existing files.

**Offline operation**: `transform` works without network connectivity, useful for testing HTML templates locally, enables work without M365 access, and doesn't consume API rate limits.

**Performance control**: Users choose when to pay transformation cost. They can disable transformations during initial sync for speed, run `transform` overnight to generate outputs, or iterate on HTML templates without syncing.

**Simpler sync logic**: `sync` doesn't need config comparison logic, transformation detection, or selective regeneration. It downloads new messages and optionally generates transformations for those messages only.

### Why Not Auto-detect in Sync

**Approach**: `sync` command detects config changes and automatically regenerates affected messages.

**Rejected because**: Complex logic (must compare current vs previous config, detect which transformations need updates), unexpected behavior (users running `sync` may not expect existing files to be modified), performance unpredictability (sync duration becomes unpredictable - might regenerate thousands of files), mixed concerns (`sync` responsible for both network I/O and local processing), and atomicity issues (hard to separate "download new messages" from "regenerate old outputs").

### Why Not Flag-Based

**Approach**: Add `--regenerate` flag to `sync` command.

**Rejected because**: Still mixed concerns (`sync` handles both download and transformation), less discoverable (users may not find the flag), and awkward semantics (`sync --regenerate` without actually syncing anything feels wrong).

### Why Not Always Regenerate

**Approach**: Every `sync` regenerates all transformations from all EML files.

**Rejected because**: Wasteful (regenerating unchanged transformations wastes CPU and disk I/O), slow (every sync becomes slow, even if only downloading few new messages), no efficiency (can't leverage incremental processing), and SSD wear (excessive writes to unchanged files).

## Consequences

### Positive

- **Clean separation**: `sync` and `transform` have distinct, understandable roles
- **Offline capability**: Transform works without network/M365 access
- **Explicit control**: Users control when regeneration happens
- **Performance**: Sync remains fast, transformation is opt-in
- **Experimentation-friendly**: Easy to iterate on templates/formats
- **Simpler code**: Each command has focused responsibility

### Negative

- **Two commands**: Users must learn both `sync` and `transform`
- **Manual step**: Users must remember to run `transform` after config changes
- **Documentation**: Need to explain when to use each command

### Neutral

- **Command count**: Adds one more command to CLI surface
- **Workflow**: Introduces two-step process (sync then transform)

## Alternatives Considered

All alternatives detailed in Rationale section above.

## User Workflows

### Initial Sync (Transformations Disabled for Speed)

```bash
# config.yaml has generate_html: false
m365-mail-mirror sync

# Later, enable HTML and generate for all messages
vim config.yaml  # Set generate_html: true
m365-mail-mirror transform
```

### Changing HTML Template

```bash
# Edit CSS file or HTML generation logic
vim html_template.html

# Regenerate all HTML files
m365-mail-mirror transform --only html --force
```

### Adding Markdown Support Later

```bash
# Enable markdown in config
vim config.yaml  # Set generate_markdown: true

# Generate markdown for all existing messages
m365-mail-mirror transform --only markdown
```

## References

- Related [ADR-003: EML-First Storage with Configurable Transformations](adr-003-eml-first-storage-with-transformations.md)
- See [DESIGN.md](../DESIGN.md) for implementation details (command interface, config versioning, parallel processing)
