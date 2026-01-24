# ADR-010: Configuration-First Transformation Options

## Status

Accepted

## Context

The transformation pipeline generates HTML, Markdown, and extracted attachments from archived EML files. Initially, transformation behavior was controlled primarily through CLI flags with hard-coded defaults. This created several issues:

**Repetitive command lines**: Users wanting consistent transformation behavior (e.g., always strip external images, always hide BCC) had to remember to include the same flags on every command invocation. This was error-prone and inconvenient for scheduled/automated syncs.

**Inconsistent defaults**: The `transform` command defaulted to enabling all transformations (HTML, Markdown, attachments), while the `sync` command required explicit flags to enable inline transformation. Users expected these commands to behave consistently.

**Missing fine-grained controls**: Important transformation options like hiding CC/BCC recipients, stripping external images, and controlling executable filtering were not exposed at all, forcing users to accept hard-coded behavior.

The tool needed a configuration hierarchy where sensible defaults live in the YAML config file, with CLI flags available as overrides for one-off variations.

## Decision

Implement a configuration-first approach for transformation options:

1. **Config file holds defaults**: All transformation options (including `generateHtml`, `generateMarkdown`, `extractAttachments`, and sub-options like `hideCc`, `stripExternalImages`) are specified in the YAML configuration file with sensible defaults.

2. **CLI flags override config**: Command-line flags use nullable types (`bool?`) so they can detect when explicitly set. When a CLI flag is provided, it overrides the config value; when omitted, the config value applies.

3. **Options flow through the pipeline**: Transformation options are encapsulated in `HtmlTransformOptions` and `AttachmentExtractOptions` classes that flow from commands through the sync/transform engines to the transformation service.

## Rationale

### Why Config-First Over CLI-First

**Reduced repetition**: Users configure their preferred transformation behavior once in the config file. Automated/scheduled syncs "just work" without requiring flags on every invocation.

**Explicit over implicit**: All available options are visible in the config file (via `config-example.yaml`), making discoverable what behaviors can be customized. CLI-only options are easily forgotten.

**Consistency**: Both `sync` (with inline transformation) and `transform` commands read from the same config structure, ensuring consistent behavior.

### Why Nullable CLI Flags

Using nullable booleans (`bool?`) for CLI options allows distinguishing between "user explicitly set false" and "user didn't specify". This enables the override semantics where:
- `--html` explicitly enables HTML transformation regardless of config
- `--html=false` explicitly disables it
- Omitting `--html` defers to config file

### Why Encapsulate in Option Classes

Grouping related options into `HtmlTransformOptions` and `AttachmentExtractOptions` classes provides:
- Type safety when passing options through layers
- Clear documentation of available options
- Easy extension for future options without changing method signatures

## Consequences

### Positive

- **Reduced command-line repetition**: Users configure once in YAML
- **Discoverable options**: All transformation options visible in config file
- **Consistent behavior**: Both sync and transform commands respect the same configuration
- **Extensible**: New options can be added to option classes without signature changes
- **Backward compatible**: CLI flags still work as overrides

### Negative

- **Two places to check**: Users debugging unexpected behavior must check both config and CLI flags
- **Nullable type complexity**: CLI option processing requires null-coalescing logic

### Neutral

- **Migration path**: Existing users who relied on CLI defaults will see different behavior; documenting the change in `config-example.yaml` provides guidance

## References

- See [config-example.yaml](../config-example.yaml) for all available transformation options
- See [DESIGN.md](../DESIGN.md) for transformation pipeline architecture
