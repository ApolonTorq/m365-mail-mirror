# Resources

This folder contains support files that are embedded into the executable and extracted at runtime for end-user access.

## Purpose

These resources provide runtime guidance and tooling that are relevant to users running the tool, but not to development of the tool itself. They are embedded as assets during the build process and can be extracted or displayed at runtime.

## Contents

- **Claude environment files** (`.md`): Claude Markdown files that provide runtime context and guidance for Claude when working with archived mailbox data
- **Runtime skills**: Skills and helpers available to end users at runtime
- **Support guides**: Documentation specific to using the tool in various scenarios

## Build Integration

Files in this folder are automatically embedded into the executable during the build process. At runtime, these files can be:
- Extracted to the user's working directory
- Displayed as inline help
- Served as contextual documentation

## IDE Configuration Templates

This folder includes configuration templates for VS Code and Cursor to optimize IDE performance with large mail archives. See the section below for details.

### Problem Statement

As m365-mail-mirror downloads and transforms emails, it creates two large folder hierarchies:

- **`eml/`** — Downloaded EML files (can contain thousands of files)
- **`transformed/`** — Regenerated outputs (HTML, Markdown, attachments)

These folders can cause IDEs to:
- Continuously scan and index for language server features
- Waste CPU/memory watching for changes users don't care about
- Blow up AI context windows with massive file trees
- Slow down search and navigation

### Configuration Files

**`.vscode/settings.json`** — VS Code workspace settings
- Prevents automatic file watching of eml/ and transformed/
- Disables language server indexing of these folders
- Disables language server auto-start and auto-formatting
- Respects `.gitignore` for search operations

Copy to `.vscode/settings.json` in your project root.

**`.copilotignore`** — GitHub Copilot exclusions
- Prevents Copilot from including eml/ and transformed/ in its context window
- Preserves token budget for actual code assistance

Copy to `.copilotignore` in your project root if using Copilot.

**`.cursorignore`** — Cursor IDE exclusions
- Similar to `.copilotignore` but for Cursor IDE
- Prevents AI context bloat from archive files

Copy to `.cursorignore` in your project root if using Cursor.

**`.gitignore.example`** — Template `.gitignore` entries
- Reference for what should be in your project's `.gitignore`
- Includes eml/, transformed/, build artifacts, IDE metadata

### Philosophy

> The IDE should not autonomously traverse, index, or scan eml/ and transformed/ folders. However, users can still manually browse, search, or use IDE features on these folders if they explicitly choose to do so.

### Setup Instructions

1. Copy `.vscode/settings.json` to `.vscode/settings.json` in your project root
2. Copy `.copilotignore` to `.copilotignore` (if using Copilot)
3. Update your `.gitignore` using `.gitignore.example` as a reference
4. Restart your IDE

### Trade-offs

**Benefits:**
- Faster IDE startup and file operations
- Reduced CPU/memory usage
- AI context preserved for source code
- Cleaner searches

**Limitations:**
- Language server features don't work in eml/transformed folders
- AI assistants won't help with files in these folders

This is acceptable because these folders contain generated outputs (EML files, HTML, Markdown), not code you typically edit.

### Technical Details

**File Watcher Exclusion (`files.watcherExclude`)** — Most impactful setting. Prevents the IDE from monitoring these folders for changes, eliminating filesystem scanning overhead.

**OmniSharp Exclusion (`omnisharp.excludeSearchPatterns`)** — Prevents the C# language server from indexing EML files as potential projects.

**AI Context Exclusion** — Different AI extensions have different mechanisms:
- GitHub Copilot uses `.copilotignore`
- Cursor IDE uses `.cursorignore`
- Other extensions: check their documentation
