# ADR-011: Outlook Web Deep Links for Message Traceability

## Status

Accepted

## Context

When users archive emails from Microsoft 365, the resulting HTML and Markdown files become disconnected from the original messages in Outlook. Users often need to return to the source message for actions that aren't possible on the archived copy:

**Reply or forward**: Archived files are read-only; responding requires accessing the original in Outlook.

**Access live attachments**: Some attachments (especially OneDrive/SharePoint links) may reference cloud resources that require authentication through Outlook.

**View threading context**: While archives capture individual messages, the full conversation threading view is often clearer in Outlook's native interface.

**Compliance verification**: Auditors may need to verify that an archived message matches what's in the live mailbox.

Without a direct link back to the source message, users must manually search in Outlook to find the corresponding email, which is time-consuming and error-prone for large archives.

## Decision

Include an optional "View in Outlook" hyperlink in transformed HTML and Markdown outputs that opens the original message directly in Outlook Web.

The link uses the message's ImmutableId (already stored during sync) to construct a deep link URL that Outlook Web can resolve to the specific message. The feature is configurable via `includeOutlookLink` in the HTML transform options, defaulting to enabled.

## Rationale

### Why ImmutableId Over GraphId

Microsoft Graph provides two identifiers for messages:
- **GraphId**: Can change when a message is moved between folders
- **ImmutableId**: Remains constant throughout the message's lifetime in Exchange

Using ImmutableId ensures links remain valid even if the message is moved after archiving. This aligns with existing design decisions to prefer ImmutableId for long-term reference (see folder sync implementation).

### Why Outlook Web Over Desktop Client

Outlook Web deep links work across platforms and don't require a local Outlook installation. The URL format `https://outlook.office.com/mail/deeplink/read/{id}` is officially supported by Microsoft and works for both personal and shared mailboxes.

### Why Shared Mailbox Support

Enterprise users frequently archive shared mailboxes (legal discovery, compliance, team archives). The URL format differs for shared mailboxes (`/mail/{mailbox}/deeplink/read/{id}`), so the implementation accepts an optional mailbox parameter to construct the correct URL.

### Why Enabled by Default

The traceability benefit applies to most use cases. Users who need completely standalone archives (no external links) can disable via `includeOutlookLink: false`.

### Why Separate Helper Class

Encapsulating URL generation in `OutlookLinkHelper` keeps the transformation service focused on output generation while making the URL format logic testable in isolation.

## Consequences

### Positive

- **Direct traceability**: One-click navigation from archived email to live Outlook message
- **Works across platforms**: Web-based link works on any device with browser access
- **Shared mailbox support**: Handles both personal and shared/delegated mailboxes
- **Configurable**: Can be disabled for standalone archives
- **Machine-readable**: Markdown front matter includes the URL for programmatic access

### Negative

- **External dependency**: Links require Microsoft 365 authentication and network access
- **Link validity**: Links become invalid if the message is permanently deleted from Outlook
- **Configuration version bump**: Regeneration of existing transformed files recommended (v4 config version)

### Neutral

- **URL encoding required**: ImmutableIds contain Base64 characters that require URL encoding
- **Rendered differently per format**: HTML uses visible anchor tag; Markdown adds both front matter and display link

## References

- Outlook Web URL format: `https://outlook.office.com/mail/deeplink/read/{ImmutableId}`
- Shared mailbox URL format: `https://outlook.office.com/mail/{mailbox}/deeplink/read/{ImmutableId}`
- See [config-example.yaml](../config-example.yaml) for `includeOutlookLink` configuration option
