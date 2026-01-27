# m365-mail-mirror - Quick Reference for AI Assistants

This file provides a concise overview of the project architecture and documentation structure for AI assistants working with this codebase.

## Project Summary

`m365-mail-mirror` is a .NET 10 command-line tool that archives Microsoft 365 mailboxes to local storage with an EML-first architecture. Email messages are downloaded as canonical EML files (RFC 2822 MIME format) from Microsoft Graph API, then optionally transformed into browsable HTML, AI-friendly Markdown, or extracted attachments. The tool supports incremental sync, offline transformation regeneration, and maintains full message fidelity in the EML archive.

**Key differentiator**: Separates downloading (network I/O) from transformation (local processing), enabling users to change output formats without re-downloading from Microsoft 365.

## Documentation Structure

- **[README.md](README.md)**: User-facing documentation
  - Installation (dotnet tool, self-contained executables)
  - Quick start guide (Azure AD registration, config, first sync)
  - Command reference (sync, transform, status, verify, auth)
  - Configuration examples and troubleshooting

- **[DESIGN.md](DESIGN.md)**: Technical architecture documentation
  - System architecture (Download → Store → Transform pipeline)
  - Technology stack (.NET 10, C#, CliFx, MimeKit, SQLite)
  - Authentication (device code flow, token storage)
  - Storage design (EML canonical format, derived outputs)
  - Transformation pipeline (HTML, Markdown, attachments)
  - Sync mechanisms (initial, incremental, delta queries)
  - Database schema (SQLite state tracking)
  - Platform considerations (Windows/macOS/Linux)
  - Testing architecture (three-tier strategy: unit, integration, E2E)
  - CI/CD integration (GitHub Actions for unit tests)

- **[CHANGELOG.md](CHANGELOG.md)**: Changelog and Work Breakdown
  - Follows Keep a Changelog format with Semantic Versioning
  - **[Unreleased] section** = Current work breakdown (unchecked items are pending work)
  - Released versions = Historical record of completed features
  - Checkbox tracking: `[x]` = complete with tests, `[ ]` = planned/in-progress
  - Guides implementation priorities and provides version history

- **[decisions/](decisions/)**: Architecture Decision Records (ADRs)
  - Historical records of key architectural decisions
  - Focus on "why" a decision was made and "what" alternatives were considered
  - Do NOT contain detailed implementation specifics (schemas, code, exact configurations)

- **This file (CLAUDE.md)**: Quick navigation for AI assistants

## Documentation Principles

### ADRs vs. DESIGN.md vs. Source Code

**Architecture Decision Records (ADRs)** capture historical decisions:

- **Purpose**: Document WHY a decision was made, WHAT was decided, and WHAT alternatives were rejected
- **Level**: High-level architectural choices that shape the system
- **Stability**: Rarely change once written (historical record)
- **Content**: Context, decision rationale, consequences, alternatives
- **Avoid**: Implementation details, code samples, exact schemas, specific configurations, detailed algorithms

**DESIGN.md** documents current implementation:

- **Purpose**: Explain HOW the system works today
- **Level**: Technical implementation details, patterns, algorithms
- **Stability**: Updated as implementation evolves
- **Content**: Architecture diagrams, data flows, schemas, algorithms, configurations, API details
- **Avoid**: Justification for architectural decisions (that belongs in ADRs)

**Source Code** is the actual implementation:

- **Purpose**: The working system
- **Level**: Executable code
- **Stability**: Changes frequently
- **Content**: Classes, functions, logic, tests

### When Writing or Updating ADRs

**DO include in ADRs**:

- Business/technical context that motivated the decision
- The architectural decision itself (at a high level)
- Key principles or constraints that guided the decision
- Alternatives considered and why they were rejected
- Consequences (positive, negative, neutral)
- General approach (e.g., "use batch processing" not "batch size of 100")

**DO NOT include in ADRs**:

- Specific schemas, table definitions, or data structures (→ DESIGN.md or source code)
- Code samples, pseudocode, or algorithms (→ DESIGN.md or source code)
- Exact configuration syntax or parameter lists (→ DESIGN.md or README.md)
- Step-by-step implementation procedures (→ DESIGN.md)
- Specific file formats or directory structures (→ DESIGN.md)
- API endpoint URLs, HTTP headers, or protocol details (→ DESIGN.md)
- Detailed error handling or edge cases (→ source code)

**Example - Good ADR content**:

> "We chose SQLite for state tracking because it's file-based (travels with the archive), requires zero configuration, and provides ACID transactions. We rejected JSON files due to lack of transactional guarantees and rejected separate database servers due to deployment complexity."

**Example - Bad ADR content** (too much implementation detail):

> ```sql
> CREATE TABLE messages (
>     graph_id TEXT PRIMARY KEY,
>     immutable_id TEXT NOT NULL UNIQUE,
>     local_path TEXT NOT NULL,
>     ...
> );
> ```
>
> This belongs in DESIGN.md, not the ADR.

### Updating Historical ADRs

When implementation details in ADRs become outdated or change:

- **Keep the ADR unchanged** - it's a historical record of the decision
- **Update DESIGN.md** with current implementation details
- If the core architectural decision changes, write a new ADR that supersedes the old one

### Guidelines for AI Assistants

When working with this codebase:

- **Read ADRs** to understand the "why" behind architectural choices
- **Read DESIGN.md** to understand current implementation patterns
- **Read source code** for exact current behavior
- **Do not treat ADRs as implementation specs** - they document decisions, not implementation
- **When implementation details conflict**, trust: Source Code > DESIGN.md > ADRs (in that order)
- **Keep CHANGELOG.md in sync**: When modifying functionality, update the `[Unreleased]` section simultaneously - add entries for new/changed features, remove entries when corresponding functionality is deleted
- **ALWAYS use test-first development**: Write a failing unit test before fixing bugs or adding features. See [Test-First Development](#test-first-development-mandatory) section - this is mandatory, not optional

## Core Architecture Concepts

### Storage Model

**EML files are canonical.** All other formats (HTML, Markdown, attachments) are derived and regenerable. This enables changing output formats without re-downloading from Microsoft 365. See [DESIGN.md](DESIGN.md) for directory structure and file naming.

### Pipeline Architecture

The tool separates downloading (network I/O via sync command) from transformation (local processing via transform command). SQLite database tracks sync state and transformation metadata, but never stores message content - only file paths and extracted headers for fast querying. See [DESIGN.md](DESIGN.md) for detailed architecture diagrams and data flows.

### Commands

- **`sync`**: Downloads EML files from Microsoft 365, optionally transforms new messages
- **`transform`**: Regenerates HTML/Markdown/attachments from local EML files (offline)
- **`status`**: Shows archive statistics and sync state
- **`verify`**: Checks integrity of EML files and transformations
- **`auth`**: Manages Microsoft 365 authentication (device code flow)

See [README.md](README.md) for command usage and options.

### Testing Strategy

**Three-tier test pyramid**: Unit tests (fast, mocked dependencies) → Integration tests (real Graph API, in-process) → E2E tests (external CLI execution).

**Unit tests** run in CI/CD on every PR without tenant access. **Integration and E2E tests** require manual device code authentication and tenant configuration (not in source control).

See [DESIGN.md](DESIGN.md) for detailed testing architecture, test project structure, and CI/CD configuration.

### Test-First Development (MANDATORY)

**When fixing bugs or adding features, you MUST follow test-first development:**

1. **Write a failing unit test first** that reproduces the bug or validates the new behavior
2. **Run the test and confirm it fails** - this proves the test actually catches the problem
3. **Then implement the fix** or feature
4. **Run the test again and confirm it passes**

This approach builds up regression guards over time and ensures tests are meaningful (a test that never failed might not be testing what you think).

**Prefer unit tests over integration tests:**

- Unit tests are fast, isolated, and run in CI without external dependencies
- Only use integration tests when you genuinely need to test against real Microsoft Graph API behavior
- If you can mock the dependency and test the logic in isolation, write a unit test

**Existing unit tests are regression guards - do not modify them lightly:**

- **If an existing unit test fails, assume the application code is wrong**, not the test
- Unit tests capture expected behavior; a failing test usually means you broke something
- **Only modify an existing unit test when functionality has intentionally changed** (e.g., a deliberate API change, new feature that changes expected output)
- Before modifying a test, ask yourself: "Did the requirements actually change, or did I introduce a bug?"
- When in doubt, fix the application code to make the test pass, not the other way around

**Privacy in test data:**

- **NEVER copy real-world example data verbatim** into unit tests (email addresses, names, message content, etc.)
- When a bug involves real data from testing or user reports, **reproduce the scenario with invented data**
- Use obviously fake data: `test@example.com`, `John Doe`, `Lorem ipsum` content, etc.
- The test should capture the structural/behavioral pattern that caused the bug, not the actual private content

**Example workflow for a bug fix:**

```
1. User reports: "Markdown cleaning breaks on emails with nested tables"
2. DO NOT copy the user's actual email content into a test
3. Create a test with invented content that has nested tables:
   - Made-up sender: "sender@example.com"
   - Synthetic content with nested table structure that triggers the bug
4. Run test → confirm it fails
5. Fix the code
6. Run test → confirm it passes
7. Commit both test and fix together
```

### Running Integration Tests

Integration tests require a configured Microsoft 365 tenant with cached authentication. Run them with:

```bash
# All platforms
dotnet test tests/IntegrationTests/M365MailMirror.IntegrationTests.csproj

# Run specific test class
dotnet test tests/IntegrationTests/M365MailMirror.IntegrationTests.csproj --filter "FullyQualifiedName~SyncCommand"

# Verbose output
dotnet test tests/IntegrationTests/M365MailMirror.IntegrationTests.csproj -v n
```

**Prerequisites**:

1. Copy [config-example.yaml](config-example.yaml) to `config.yaml` in the repository root
2. Configure `clientId` (Azure AD app registration) and `mailbox` (target mailbox email)
3. Run `dotnet run --project src/Cli auth login` to authenticate via device code flow

The tests read authentication from `config.yaml` in the repository root. See [config-example.yaml](config-example.yaml) for all available options and Azure AD setup instructions.

**Test settings file**: [tests/IntegrationTests/integration-test-settings.jsonc](tests/IntegrationTests/integration-test-settings.jsonc)

```jsonc
{
  "logLevel": "info",        // Options: debug, info, warning, error, none
  "logFileName": "integration-test.log"  // Set to null to disable file logging
}
```

**Log file location**: `tests/IntegrationTests/output/status/integration-test.log`

The log file contains detailed output from all test runs including sync progress, transformation results, and a summary of passed/failed tests. When diagnosing test failures, check this log for `ERR` entries and stack traces.

## Key Design Decisions

See [decisions/](decisions/) for full Architecture Decision Records (ADRs):

## Technology Stack

- **Runtime**: .NET 10, C# 13
- **CLI Framework**: CliFx
- **Microsoft Graph**: Microsoft.Graph SDK (v5.x)
- **MIME Parsing**: MimeKit
- **Database**: Microsoft.Data.Sqlite (embedded SQLite)
- **Configuration**: YamlDotNet
- **Authentication**: Microsoft.Identity.Client (MSAL)
- **Testing**: xUnit, Moq, FluentAssertions, CliWrap

See [DESIGN.md](DESIGN.md) for detailed technology usage patterns and platform considerations.

## Implementation Reference

When implementing features, refer to:

- **Storage patterns**: See [DESIGN.md](DESIGN.md) sections on Storage Design, Directory Structure, File Naming
- **Database schema**: See [DESIGN.md](DESIGN.md) section on State Database
- **API usage**: See [DESIGN.md](DESIGN.md) section on API Interaction Design
- **Security**: See [DESIGN.md](DESIGN.md) section on Security Architecture
- **Platform-specific code**: See [DESIGN.md](DESIGN.md) section on Platform Considerations
- **Testing patterns**: See [DESIGN.md](DESIGN.md) section on Testing Architecture

## Quick Reference Links

- **User documentation**: [README.md](README.md)
- **Technical design**: [DESIGN.md](DESIGN.md)
- **Changelog / Work breakdown**: [CHANGELOG.md](CHANGELOG.md)
- **Architecture decisions**: [decisions/](decisions/)
- **Microsoft Graph API**: https://learn.microsoft.com/en-us/graph/
- **MimeKit documentation**: https://mimekit.net/
