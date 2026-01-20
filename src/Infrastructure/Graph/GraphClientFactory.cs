using Azure.Core;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;
using M365MailMirror.Core.Graph;
using M365MailMirror.Core.Logging;

namespace M365MailMirror.Infrastructure.Graph;

/// <summary>
/// Factory for creating configured Microsoft Graph client instances.
/// </summary>
public class GraphClientFactory : IGraphClientFactory
{
    private readonly IAppLogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphClientFactory"/> class.
    /// </summary>
    public GraphClientFactory(IAppLogger? logger = null)
    {
        _logger = logger ?? LoggerFactory.CreateLogger<GraphClientFactory>();
    }

    /// <inheritdoc />
    public IGraphMailClient CreateClient(string accessToken)
    {
        _logger.Debug("Creating Graph client");

        var tokenProvider = new StaticAccessTokenProvider(accessToken);
        var authProvider = new BaseBearerTokenAuthenticationProvider(tokenProvider);
        var graphClient = new GraphServiceClient(authProvider);

        return new GraphMailClient(graphClient, _logger);
    }
}

/// <summary>
/// Access token provider that uses a pre-obtained token.
/// </summary>
internal class StaticAccessTokenProvider : IAccessTokenProvider
{
    private readonly string _accessToken;

    public StaticAccessTokenProvider(string accessToken)
    {
        _accessToken = accessToken;
    }

    public AllowedHostsValidator AllowedHostsValidator { get; } = new AllowedHostsValidator();

    public Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_accessToken);
    }
}
