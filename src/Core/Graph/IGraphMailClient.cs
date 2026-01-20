namespace M365MailMirror.Core.Graph;

/// <summary>
/// Abstraction over Microsoft Graph API for mail operations.
/// </summary>
public interface IGraphMailClient
{
    /// <summary>
    /// Gets the authenticated user's email address.
    /// </summary>
    Task<string> GetUserEmailAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all mail folders for the specified mailbox.
    /// </summary>
    /// <param name="mailbox">The mailbox email address. If null, uses the authenticated user's mailbox.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of mail folders.</returns>
    Task<IReadOnlyList<AppMailFolder>> GetFoldersAsync(string? mailbox = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets messages from a folder using delta query for incremental sync.
    /// </summary>
    /// <param name="folderId">The Graph folder ID.</param>
    /// <param name="deltaToken">Optional delta token from previous sync. If null, returns all messages.</param>
    /// <param name="mailbox">The mailbox email address. If null, uses the authenticated user's mailbox.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The delta query result containing messages and the new delta token.</returns>
    Task<DeltaQueryResult<MessageInfo>> GetMessagesDeltaAsync(
        string folderId,
        string? deltaToken = null,
        string? mailbox = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the raw MIME content of a message as EML.
    /// </summary>
    /// <param name="messageId">The Graph message ID.</param>
    /// <param name="mailbox">The mailbox email address. If null, uses the authenticated user's mailbox.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw MIME content as a stream.</returns>
    Task<Stream> DownloadMessageMimeAsync(
        string messageId,
        string? mailbox = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets messages from a folder received since a specific date.
    /// Used as a fallback when delta tokens are invalid or expired.
    /// </summary>
    /// <param name="folderId">The Graph folder ID.</param>
    /// <param name="sinceDate">Only return messages received on or after this date.</param>
    /// <param name="mailbox">The mailbox email address. If null, uses the authenticated user's mailbox.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Messages received since the specified date.</returns>
    Task<IReadOnlyList<MessageInfo>> GetMessagesSinceDateAsync(
        string folderId,
        DateTimeOffset sinceDate,
        string? mailbox = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a mail folder.
/// </summary>
public class AppMailFolder
{
    /// <summary>
    /// The Graph folder ID.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The folder display name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// The parent folder ID, if any.
    /// </summary>
    public string? ParentFolderId { get; init; }

    /// <summary>
    /// The full path of the folder (e.g., "Inbox/Important").
    /// </summary>
    public required string FullPath { get; init; }

    /// <summary>
    /// The total number of items in the folder.
    /// </summary>
    public int TotalItemCount { get; init; }

    /// <summary>
    /// The number of unread items in the folder.
    /// </summary>
    public int UnreadItemCount { get; init; }
}

/// <summary>
/// Contains basic message information from Graph API.
/// </summary>
public class MessageInfo
{
    /// <summary>
    /// The Graph message ID.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The immutable message ID (stable across moves).
    /// </summary>
    public string? ImmutableId { get; init; }

    /// <summary>
    /// The internet message ID (from the email headers).
    /// </summary>
    public string? InternetMessageId { get; init; }

    /// <summary>
    /// The message subject.
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>
    /// The sender email address.
    /// </summary>
    public string? From { get; init; }

    /// <summary>
    /// When the message was received.
    /// </summary>
    public DateTimeOffset ReceivedDateTime { get; init; }

    /// <summary>
    /// Whether the message has attachments.
    /// </summary>
    public bool HasAttachments { get; init; }

    /// <summary>
    /// The parent folder ID.
    /// </summary>
    public string? ParentFolderId { get; init; }

    /// <summary>
    /// Whether this message was deleted (for delta queries).
    /// Indicated by @removed with reason "deleted".
    /// </summary>
    public bool IsDeleted { get; init; }

    /// <summary>
    /// Whether this message was moved to another folder (for delta queries).
    /// Indicated by @removed with reason "changed".
    /// </summary>
    public bool IsMoved { get; init; }

    /// <summary>
    /// The new parent folder ID if the message was moved.
    /// </summary>
    public string? NewParentFolderId { get; init; }
}

/// <summary>
/// Result of a delta query containing items and pagination/delta token.
/// </summary>
/// <typeparam name="T">The type of items returned.</typeparam>
public class DeltaQueryResult<T>
{
    /// <summary>
    /// The items returned by the query.
    /// </summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>
    /// The delta token for the next incremental sync.
    /// </summary>
    public string? DeltaToken { get; init; }

    /// <summary>
    /// Whether there are more pages of results.
    /// </summary>
    public bool HasMorePages { get; init; }

    /// <summary>
    /// The link to the next page of results, if any.
    /// </summary>
    public string? NextPageLink { get; init; }
}
