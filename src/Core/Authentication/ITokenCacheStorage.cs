namespace M365MailMirror.Core.Authentication;

/// <summary>
/// Provides platform-specific secure storage for authentication tokens.
/// </summary>
public interface ITokenCacheStorage
{
    /// <summary>
    /// Gets a description of the storage location (for status display).
    /// </summary>
    string StorageDescription { get; }

    /// <summary>
    /// Reads the cached token data.
    /// </summary>
    /// <returns>The cached token data, or null if no cache exists.</returns>
    Task<byte[]?> ReadAsync();

    /// <summary>
    /// Writes token data to the cache.
    /// </summary>
    /// <param name="data">The token data to cache.</param>
    Task WriteAsync(byte[] data);

    /// <summary>
    /// Clears all cached token data.
    /// </summary>
    Task ClearAsync();

    /// <summary>
    /// Checks whether cached token data exists.
    /// </summary>
    /// <returns>True if cache exists, false otherwise.</returns>
    Task<bool> ExistsAsync();
}
