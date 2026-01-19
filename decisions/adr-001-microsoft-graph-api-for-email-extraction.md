# ADR-001: Microsoft Graph API for Email Extraction

## Status

Accepted

## Context

We need to programmatically extract email messages from Microsoft 365 mailboxes in EML format for ongoing backup and archival. Requirements include:

- Incremental synchronization (subsequent runs transfer only new emails)
- Operation on behalf of specific users (delegated permissions)
- Handling of large mailboxes (tens to hundreds of gigabytes)
- Preservation of full message fidelity for downstream transformation to HTML and Markdown

Multiple APIs exist for accessing Microsoft 365 mailboxes, each with different characteristics, support lifecycles, and capabilities.

## Decision

Use Microsoft Graph API as the sole interface for email extraction.

## Rationale

### Why Microsoft Graph API

**Native MIME/EML extraction**: Graph API's `$value` endpoint returns raw RFC 2822 MIME content directly, which can be saved as `.eml` files without conversion or fidelity loss.

**Built-in delta queries**: Graph API provides native delta query functionality for incremental synchronization, tracking message additions, deletions, and folder moves without application-level state management.

**Active development with long-term support**: Microsoft has committed to Graph API as the modern interface for Microsoft 365 services, with ongoing feature development and long-term support guarantees.

**Per-mailbox rate limits**: Rate limiting is applied per application per mailbox, enabling parallel extraction across multiple mailboxes without interference.

**Well-documented SDK**: The official C# SDK (Microsoft.Graph) provides typed access to endpoints with comprehensive documentation.

### Why Not Exchange Web Services (EWS)

Microsoft announced EWS will be blocked for Exchange Online in October 2026. The EWS Managed API library receives only security fixes with no new feature development. Building on EWS would require migration within the system's expected lifespan.

### Why Not IMAP

IMAP requires explicit per-mailbox enablement in Microsoft 365 and lacks built-in change tracking. OAuth 2.0 authentication is supported but the protocol offers no native delta/incremental sync mechanism, requiring application-level tracking of message state (UIDs, folder scanning).

### Why Not Compliance Center / eDiscovery

Content Search exports to PST format only and operates asynchronously with multi-hour delays for large datasets. It is designed for legal/compliance scenarios with admin-level access rather than programmatic user-level backup workflows.

## Consequences

### Positive

- **Single API surface**: Reduces complexity by relying on one well-supported interface
- **Efficient incremental sync**: Delta queries enable efficient updates without application-level message tracking or folder scanning
- **Native MIME extraction**: Preserves message fidelity without conversion
- **Horizontal scaling**: Per-mailbox rate limits enable concurrent processing across mailboxes
- **Long-term viability**: Microsoft's strategic API with ongoing investment

### Negative

- **Archive mailbox access**: In-Place Archive access requires beta endpoint or fallback to EWS until Graph GA support
- **Delta token expiration**: Delta tokens can expire unpredictably, requiring robust resync handling
- **Reference attachments**: Cloud-based attachments (OneDrive/SharePoint links) require additional fetching logic
- **Protected messages**: IRM/RMS protected messages require separate decryption infrastructure if content access is needed

### Neutral

- **Concurrent connection limits**: 4 concurrent connections per mailbox bounds parallelism within a single mailbox
- **Per-folder delta tracking**: Requires enumerating and managing state for all folders separately

## Alternatives Considered

All major alternatives are documented in the Rationale section above (EWS, IMAP, Compliance Center).

## References

- Microsoft Graph Mail API Overview: https://learn.microsoft.com/en-us/graph/api/resources/mail-api-overview
- Get Message MIME Content: https://learn.microsoft.com/en-us/graph/api/message-get#example-2-get-mime-content
- Delta Query for Messages: https://learn.microsoft.com/en-us/graph/delta-query-messages
- Throttling Limits: https://learn.microsoft.com/en-us/graph/throttling-limits
- EWS Retirement: https://devblogs.microsoft.com/microsoft365dev/retirement-of-exchange-web-services-in-exchange-online/
- See [DESIGN.md](../DESIGN.md) for implementation details (endpoints, headers, rate limiting, authentication)
