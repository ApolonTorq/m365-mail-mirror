# ADR-002: Device Code Flow Authentication

## Status

Accepted

## Context

The application requires OAuth 2.0 authentication to access Microsoft 365 mailboxes via Microsoft Graph API. The tool is designed as a command-line utility that may run in diverse environments:

- Interactive terminal sessions
- Scheduled/automated environments (cron, Task Scheduler)
- Environments without a web browser
- Server environments or headless systems
- SSH sessions without display forwarding

Authentication requirements include:

- Support for both personal Microsoft accounts and work/school accounts
- No client secrets to manage or secure
- Long-lived tokens for unattended operation
- Initial interactive authentication followed by automatic token refresh
- Delegated permissions (user context, not application-only)

Multiple OAuth 2.0 flows exist with different characteristics for client type, user interaction, and deployment scenarios.

## Decision

Use OAuth 2.0 Device Code Flow exclusively for authentication.

## Rationale

### Why Device Code Flow

**No client secrets required**: Device code flow is a public client flow that doesn't require client secrets, eliminating the need for secure secret storage, rotation, and distribution.

**Browser-independent**: Users authenticate by visiting a URL on any device and entering a short code. This works in SSH sessions, Docker containers, environments where launching a browser programmatically is unreliable, and systems where the user doesn't have browser access from the command line.

**Works across account types**: Supports personal Microsoft accounts, work or school accounts (Azure AD), and multi-factor authentication (MFA) scenarios without additional configuration.

**Long-lived refresh tokens**: After initial authentication, the tool receives a refresh token valid for up to 90 days (or until revoked), enabling scheduled syncs without user interaction.

### Why Not Authorization Code Flow

Authorization code flow requires client secrets for confidential clients or PKCE for public clients, plus programmatic browser launch and localhost callback handling. This adds complexity and fails in many deployment scenarios where browser integration is unavailable or unreliable.

### Why Not Client Credentials Flow

Client credentials flow (application-only permissions) requires admin consent for the Azure AD application and `Mail.Read` application permission (read all users' mail). This is inappropriate for a personal backup tool where users should only access their own mailbox.

### Why Not Interactive Browser Flow

Interactive browser flow (used by many desktop applications) provides the best UX in GUI environments but doesn't work in headless/server environments, requires complex browser integration, fails in containerized deployments, and is incompatible with a CLI-first design philosophy.

## Consequences

### Positive

- **No secrets to manage**: Users don't need to handle client secrets
- **Works everywhere**: Compatible with all deployment scenarios (interactive, headless, SSH, containers)
- **Simple deployment**: No need for localhost HTTP servers or browser integration
- **Cross-platform**: Standard OAuth flow supported by Microsoft Identity Platform
- **Secure token storage**: Tokens stored in OS-secured credential stores, never in plain text files

### Negative

- **Initial UX friction**: Users must visit a URL and type a code (not seamless)
- **Two-device scenario**: Users without clipboard access must manually type URL and code
- **Refresh token lifetime**: Maximum 90 days; users running sync less frequently must re-authenticate
- **No silent renewal**: If refresh token expires, user must complete interactive auth again

### Neutral

- **User-owned app registrations**: Each user must create their own Azure AD app. This provides user control but adds setup steps.
- **Device code support**: Relies on Microsoft maintaining device code flow support. As of 2025, this is a stable, widely-used flow.

## Alternatives Considered

All major alternatives are documented in the Rationale section above (Authorization Code with PKCE, Client Credentials, Interactive Browser Flow).

## References

- Microsoft Identity Platform Device Code Flow: https://learn.microsoft.com/en-us/azure/active-directory/develop/v2-oauth2-device-code
- OAuth 2.0 Device Authorization Grant (RFC 8628): https://datatracker.ietf.org/doc/html/rfc8628
- Microsoft Graph API Permissions: https://learn.microsoft.com/en-us/graph/permissions-reference
- See [DESIGN.md](../DESIGN.md) for implementation details (token storage, refresh logic, unattended mode)
