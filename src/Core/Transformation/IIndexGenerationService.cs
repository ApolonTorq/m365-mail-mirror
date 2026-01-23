namespace M365MailMirror.Core.Transform;

/// <summary>
/// Service for generating index files (index.html and index.md) for navigating the email archive.
/// </summary>
public interface IIndexGenerationService
{
    /// <summary>
    /// Generates index files for the entire archive.
    /// Creates index files at each level of the hierarchy:
    /// - Root level: Links to all mail folders
    /// - Folder level: Links to years
    /// - Year level: Links to months
    /// - Month level: Table of emails
    /// </summary>
    /// <param name="options">Options controlling which index types to generate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success and counts of generated indexes.</returns>
    Task<IndexGenerationResult> GenerateIndexesAsync(
        IndexGenerationOptions options,
        CancellationToken cancellationToken = default);
}
