using System.Globalization;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using M365MailMirror.Core.Graph;
using M365MailMirror.Core.Logging;

namespace M365MailMirror.Infrastructure.Graph;

/// <summary>
/// Microsoft Graph API client for mail operations.
/// </summary>
public class GraphMailClient : IGraphMailClient
{
    private readonly GraphServiceClient _graphClient;
    private readonly IAppLogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphMailClient"/> class.
    /// </summary>
    public GraphMailClient(GraphServiceClient graphClient, IAppLogger? logger = null)
    {
        _graphClient = graphClient;
        _logger = logger ?? LoggerFactory.CreateLogger<GraphMailClient>();
    }

    /// <inheritdoc />
    public async Task<string> GetUserEmailAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _graphClient.Me.GetAsync(config =>
            {
                config.QueryParameters.Select = ["mail", "userPrincipalName"];
            }, cancellationToken);

            return user?.Mail ?? user?.UserPrincipalName ?? throw new InvalidOperationException("Could not determine user email");
        }
        catch (ODataError ex)
        {
            _logger.Error(ex, "Graph API error getting user email: {0}", ex.Error?.Message ?? ex.Message);
            throw new GraphApiException($"Failed to get user email: {ex.Error?.Message ?? ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AppMailFolder>> GetFoldersAsync(string? mailbox = null, CancellationToken cancellationToken = default)
    {
        _logger.Debug("Getting mail folders for mailbox: {0}", mailbox ?? "(authenticated user)");

        var folders = new List<AppMailFolder>();

        try
        {
            // Get top-level folders
            var response = mailbox != null
                ? await _graphClient.Users[mailbox].MailFolders.GetAsync(config =>
                {
                    config.QueryParameters.Select = ["id", "displayName", "parentFolderId", "totalItemCount", "unreadItemCount"];
                    config.QueryParameters.Top = 100;
                    config.QueryParameters.IncludeHiddenFolders = "true";
                    config.Headers.Add("Prefer", "IdType=\"ImmutableId\"");
                }, cancellationToken)
                : await _graphClient.Me.MailFolders.GetAsync(config =>
                {
                    config.QueryParameters.Select = ["id", "displayName", "parentFolderId", "totalItemCount", "unreadItemCount"];
                    config.QueryParameters.Top = 100;
                    config.QueryParameters.IncludeHiddenFolders = "true";
                    config.Headers.Add("Prefer", "IdType=\"ImmutableId\"");
                }, cancellationToken);

            if (response?.Value != null)
            {
                foreach (var folder in response.Value)
                {
                    await ProcessFolderRecursiveAsync(folder, "", mailbox, folders, cancellationToken);
                }
            }

            _logger.Debug("Found {0} mail folders", folders.Count);
            return folders;
        }
        catch (ODataError ex)
        {
            _logger.Error(ex, "Graph API error getting folders: {0}", ex.Error?.Message ?? ex.Message);
            throw new GraphApiException($"Failed to get mail folders: {ex.Error?.Message ?? ex.Message}", ex);
        }
    }

    private async Task ProcessFolderRecursiveAsync(
        Microsoft.Graph.Models.MailFolder graphFolder,
        string parentPath,
        string? mailbox,
        List<AppMailFolder> folders,
        CancellationToken cancellationToken)
    {
        var folderPath = string.IsNullOrEmpty(parentPath)
            ? graphFolder.DisplayName ?? "Unknown"
            : $"{parentPath}/{graphFolder.DisplayName}";

        folders.Add(new AppMailFolder
        {
            Id = graphFolder.Id ?? throw new InvalidOperationException("Folder ID is null"),
            DisplayName = graphFolder.DisplayName ?? "Unknown",
            ParentFolderId = graphFolder.ParentFolderId,
            FullPath = folderPath,
            TotalItemCount = graphFolder.TotalItemCount ?? 0,
            UnreadItemCount = graphFolder.UnreadItemCount ?? 0
        });

        // Get child folders
        try
        {
            var childResponse = mailbox != null
                ? await _graphClient.Users[mailbox].MailFolders[graphFolder.Id].ChildFolders.GetAsync(config =>
                {
                    config.QueryParameters.Select = ["id", "displayName", "parentFolderId", "totalItemCount", "unreadItemCount"];
                    config.QueryParameters.Top = 100;
                    config.Headers.Add("Prefer", "IdType=\"ImmutableId\"");
                }, cancellationToken)
                : await _graphClient.Me.MailFolders[graphFolder.Id].ChildFolders.GetAsync(config =>
                {
                    config.QueryParameters.Select = ["id", "displayName", "parentFolderId", "totalItemCount", "unreadItemCount"];
                    config.QueryParameters.Top = 100;
                    config.Headers.Add("Prefer", "IdType=\"ImmutableId\"");
                }, cancellationToken);

            if (childResponse?.Value != null)
            {
                foreach (var childFolder in childResponse.Value)
                {
                    await ProcessFolderRecursiveAsync(childFolder, folderPath, mailbox, folders, cancellationToken);
                }
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            // No child folders - this is fine
        }
    }

    /// <inheritdoc />
    public async Task<DeltaQueryResult<MessageInfo>> GetMessagesDeltaAsync(
        string folderId,
        string? deltaToken = null,
        string? mailbox = null,
        CancellationToken cancellationToken = default)
    {
        _logger.Debug("Getting messages delta for folder {0}, deltaToken: {1}", folderId, deltaToken != null ? "(provided)" : "(none)");

        var messages = new List<MessageInfo>();
        string? nextLink = null;
        string? deltaLink = null;

        try
        {
            IEnumerable<Message>? messageValues = null;

            // If we have a deltaToken, it's either a nextLink (for pagination) or deltaLink (for incremental sync)
            // Both are full URLs that we need to follow using WithUrl
            if (!string.IsNullOrEmpty(deltaToken))
            {
                // Follow the continuation URL (either nextLink or deltaLink)
                var response = await _graphClient.Users[mailbox ?? "me"].MailFolders[folderId].Messages.Delta
                    .WithUrl(deltaToken)
                    .GetAsDeltaGetResponseAsync(config =>
                    {
                        config.Headers.Add("Prefer", "IdType=\"ImmutableId\"");
                    }, cancellationToken);

                messageValues = response?.Value;
                nextLink = response?.OdataNextLink;
                deltaLink = response?.OdataDeltaLink;
            }
            else if (mailbox != null)
            {
                // Initial delta query for a specific mailbox
                var response = await _graphClient.Users[mailbox].MailFolders[folderId].Messages.Delta.GetAsDeltaGetResponseAsync(config =>
                {
                    config.QueryParameters.Select = ["id", "internetMessageId", "subject", "from", "receivedDateTime", "hasAttachments", "parentFolderId"];
                    config.Headers.Add("Prefer", "IdType=\"ImmutableId\"");
                }, cancellationToken);

                messageValues = response?.Value;
                nextLink = response?.OdataNextLink;
                deltaLink = response?.OdataDeltaLink;
            }
            else
            {
                // Initial delta query for the authenticated user's mailbox
                var response = await _graphClient.Me.MailFolders[folderId].Messages.Delta.GetAsDeltaGetResponseAsync(config =>
                {
                    config.QueryParameters.Select = ["id", "internetMessageId", "subject", "from", "receivedDateTime", "hasAttachments", "parentFolderId"];
                    config.Headers.Add("Prefer", "IdType=\"ImmutableId\"");
                }, cancellationToken);

                messageValues = response?.Value;
                nextLink = response?.OdataNextLink;
                deltaLink = response?.OdataDeltaLink;
            }

            if (messageValues != null)
            {
                foreach (var message in messageValues)
                {
                    var isDeleted = false;
                    var isMoved = false;
                    string? newParentFolderId = null;

                    // Parse @removed annotation to determine if message was deleted or moved
                    if (message.AdditionalData?.TryGetValue("@removed", out var removedValue) == true)
                    {
                        if (removedValue is System.Text.Json.JsonElement jsonElement)
                        {
                            if (jsonElement.TryGetProperty("reason", out var reasonElement))
                            {
                                var reason = reasonElement.GetString();
                                if (string.Equals(reason, "deleted", StringComparison.OrdinalIgnoreCase))
                                {
                                    isDeleted = true;
                                }
                                else if (string.Equals(reason, "changed", StringComparison.OrdinalIgnoreCase))
                                {
                                    isMoved = true;
                                    // The message's ParentFolderId shows the new folder
                                    newParentFolderId = message.ParentFolderId;
                                }
                            }
                        }
                    }

                    messages.Add(new MessageInfo
                    {
                        Id = message.Id ?? throw new InvalidOperationException("Message ID is null"),
                        ImmutableId = message.Id, // With IdType="ImmutableId" header, Id is already immutable
                        InternetMessageId = message.InternetMessageId,
                        Subject = message.Subject,
                        From = message.From?.EmailAddress?.Address,
                        ReceivedDateTime = message.ReceivedDateTime ?? DateTimeOffset.MinValue,
                        HasAttachments = message.HasAttachments ?? false,
                        ParentFolderId = message.ParentFolderId,
                        IsDeleted = isDeleted,
                        IsMoved = isMoved,
                        NewParentFolderId = newParentFolderId
                    });
                }
            }

            var hasMorePages = nextLink != null;

            _logger.Debug("Retrieved {0} messages, hasMorePages: {1}", messages.Count, hasMorePages);

            return new DeltaQueryResult<MessageInfo>
            {
                Items = messages,
                DeltaToken = deltaLink ?? nextLink,
                HasMorePages = hasMorePages,
                NextPageLink = nextLink
            };
        }
        catch (ODataError ex)
        {
            _logger.Error(ex, "Graph API error getting messages delta: {0}", ex.Error?.Message ?? ex.Message);
            throw new GraphApiException($"Failed to get messages: {ex.Error?.Message ?? ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadMessageMimeAsync(
        string messageId,
        string? mailbox = null,
        CancellationToken cancellationToken = default)
    {
        _logger.Debug("Downloading MIME content for message {0}", messageId);

        try
        {
            var stream = mailbox != null
                ? await _graphClient.Users[mailbox].Messages[messageId].Content.GetAsync(cancellationToken: cancellationToken)
                : await _graphClient.Me.Messages[messageId].Content.GetAsync(cancellationToken: cancellationToken);

            if (stream == null)
            {
                throw new InvalidOperationException($"No MIME content returned for message {messageId}");
            }

            return stream;
        }
        catch (ODataError ex)
        {
            _logger.Error(ex, "Graph API error downloading message MIME: {0}", ex.Error?.Message ?? ex.Message);
            throw new GraphApiException($"Failed to download message: {ex.Error?.Message ?? ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MessageInfo>> GetMessagesSinceDateAsync(
        string folderId,
        DateTimeOffset sinceDate,
        string? mailbox = null,
        CancellationToken cancellationToken = default)
    {
        _logger.Debug("Getting messages since {0} for folder {1}", sinceDate, folderId);

        var messages = new List<MessageInfo>();
        var filterDate = sinceDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        try
        {
            string? nextLink = null;

            do
            {
                IEnumerable<Message>? messageValues = null;

                if (mailbox != null)
                {
                    var response = await _graphClient.Users[mailbox].MailFolders[folderId].Messages.GetAsync(config =>
                    {
                        config.QueryParameters.Select = ["id", "internetMessageId", "subject", "from", "receivedDateTime", "hasAttachments", "parentFolderId"];
                        config.QueryParameters.Filter = $"receivedDateTime ge {filterDate}";
                        config.QueryParameters.Top = 100;
                        config.QueryParameters.Orderby = ["receivedDateTime desc"];
                        config.Headers.Add("Prefer", "IdType=\"ImmutableId\"");
                    }, cancellationToken);

                    messageValues = response?.Value;
                    nextLink = response?.OdataNextLink;
                }
                else
                {
                    var response = await _graphClient.Me.MailFolders[folderId].Messages.GetAsync(config =>
                    {
                        config.QueryParameters.Select = ["id", "internetMessageId", "subject", "from", "receivedDateTime", "hasAttachments", "parentFolderId"];
                        config.QueryParameters.Filter = $"receivedDateTime ge {filterDate}";
                        config.QueryParameters.Top = 100;
                        config.QueryParameters.Orderby = ["receivedDateTime desc"];
                        config.Headers.Add("Prefer", "IdType=\"ImmutableId\"");
                    }, cancellationToken);

                    messageValues = response?.Value;
                    nextLink = response?.OdataNextLink;
                }

                if (messageValues != null)
                {
                    foreach (var message in messageValues)
                    {
                        messages.Add(new MessageInfo
                        {
                            Id = message.Id ?? throw new InvalidOperationException("Message ID is null"),
                            ImmutableId = message.Id,
                            InternetMessageId = message.InternetMessageId,
                            Subject = message.Subject,
                            From = message.From?.EmailAddress?.Address,
                            ReceivedDateTime = message.ReceivedDateTime ?? DateTimeOffset.MinValue,
                            HasAttachments = message.HasAttachments ?? false,
                            ParentFolderId = message.ParentFolderId,
                            IsDeleted = false
                        });
                    }
                }

            } while (nextLink != null);

            _logger.Debug("Retrieved {0} messages since {1}", messages.Count, sinceDate);
            return messages;
        }
        catch (ODataError ex)
        {
            _logger.Error(ex, "Graph API error getting messages by date: {0}", ex.Error?.Message ?? ex.Message);
            throw new GraphApiException($"Failed to get messages: {ex.Error?.Message ?? ex.Message}", ex);
        }
    }
}

/// <summary>
/// Exception thrown when a Graph API operation fails.
/// </summary>
public class GraphApiException : Exception
{
    public GraphApiException(string message) : base(message) { }
    public GraphApiException(string message, Exception innerException) : base(message, innerException) { }
}
