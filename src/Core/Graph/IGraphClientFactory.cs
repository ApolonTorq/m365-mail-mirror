namespace M365MailMirror.Core.Graph;

/// <summary>
/// Factory for creating configured Microsoft Graph client instances.
/// </summary>
public interface IGraphClientFactory
{
    /// <summary>
    /// Creates a Graph client using the provided access token.
    /// </summary>
    /// <param name="accessToken">The OAuth access token for Graph API authentication.</param>
    /// <returns>A configured Graph client interface.</returns>
    IGraphMailClient CreateClient(string accessToken);
}
