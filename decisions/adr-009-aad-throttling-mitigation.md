# ADR-009: Azure AD Throttling Mitigation

## Status

Accepted

## Context

The application authenticates with Microsoft 365 using OAuth 2.0 device code flow (see ADR-002) and acquires access tokens through Microsoft Authentication Library (MSAL). During sync operations, the Microsoft Graph SDK requests tokens for each API call through a `TokenCredential` implementation.

**Observed problems**:

**Excessive MSAL calls**: Each Graph API request triggered token acquisition through MSAL, even when the current token was still valid. For large mailboxes with thousands of messages, this resulted in thousands of MSAL calls per sync session.

**Azure AD throttling**: The volume of token requests triggered Azure Active Directory's rate limiting. AAD throttling manifests as `MsalUiRequiredException` errors that appear to require re-authentication, even when credentials are valid. This caused sync operations to fail mid-way through large mailboxes, making test runs impossible to complete.

**Status check triggering throttling**: The `auth status` command called `AcquireTokenSilent` to verify token validity. Running status checks between sync attempts compounded the throttling problem.

The application needed to reduce AAD token requests and gracefully handle throttling when it occurs.

## Decision

Implement a multi-layered approach to minimize Azure AD token requests and handle throttling gracefully:

1. **In-memory token caching**: Cache acquired tokens in the `DelegateTokenCredential` layer, returning cached tokens for Graph SDK requests without calling MSAL.

2. **Proactive token refresh**: Refresh tokens 5 minutes before expiry to avoid edge cases where a token expires mid-request.

3. **Retry with exponential backoff**: When throttling is detected, retry token acquisition with increasing delays (10s, 20s, 30s) before failing.

4. **Cache-only status checks**: The `GetStatusAsync` method reads from local MSAL cache only, never triggering network calls to AAD for status queries.

## Rationale

### Why In-Memory Token Caching

**Operational necessity**: Without token caching, sync operations could not complete on large mailboxes. The number of Graph API calls (one per message for MIME content) multiplied by MSAL overhead exceeded AAD's rate limits.

**MSAL limitation**: While MSAL has its own token cache, the `AcquireTokenSilent` call still involves cache lookup overhead and potential network calls for token refresh. An application-level cache with known-valid tokens eliminates this overhead entirely for most requests.

**Token lifetime predictability**: Access tokens have a known expiry time. Caching and reusing tokens until near expiry is safe and reduces unnecessary round-trips.

### Why 5-Minute Refresh Buffer

Refreshing exactly at expiry risks race conditions where a token expires between validation and use. A 5-minute buffer ensures tokens are refreshed proactively while still maximizing cache hit rate.

### Why Retry Logic

**Graceful degradation**: Throttling is transient. Rather than failing immediately and requiring user intervention, retry logic allows the sync to self-recover from brief throttling periods.

**Exponential backoff**: Increasing delays (10s, 20s, 30s) give AAD time to clear the rate limit while not extending total sync time excessively for persistent throttling.

### Why Cache-Only Status Checks

The `auth status` command exists for diagnostic purposes. It should not contribute to throttling or fail due to throttling. Reading from local MSAL cache provides sufficient information (account exists, token cached) without network calls. Actual token validity is verified when sync starts.

## Consequences

### Positive

- **Sync operations complete**: Test runs and real syncs can complete on large mailboxes without AAD throttling failures
- **Reduced AAD load**: Dramatically fewer token requests sent to Azure AD
- **Graceful recovery**: Transient throttling is handled automatically without user intervention
- **Status command reliability**: Status checks never fail due to throttling or contribute to throttling

### Negative

- **Stale status possible**: `auth status` may report authenticated when token has actually expired, since it doesn't validate with AAD. Actual expiry is detected at sync time.
- **Retry latency**: When throttling occurs, retries add up to 60 seconds of delay before failure
- **Memory usage**: Tokens are cached in memory (minimal impact - single token object)

### Neutral

- **Extends ADR-002**: This decision builds on the device code flow authentication architecture, adding operational resilience
- **Test compatibility**: Integration tests must account for token caching behavior when verifying authentication flows

## References

- Azure AD Throttling Guidance: https://learn.microsoft.com/en-us/azure/active-directory/develop/resilience-error-handling
- MSAL Token Caching: https://learn.microsoft.com/en-us/azure/active-directory/develop/msal-net-token-cache-serialization
- Related: [ADR-002: Device Code Flow Authentication](adr-002-device-code-flow-authentication.md)
- See [DESIGN.md](../DESIGN.md) for implementation details (token flow diagrams, retry configuration)
