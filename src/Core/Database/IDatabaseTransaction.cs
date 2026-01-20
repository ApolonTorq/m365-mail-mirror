namespace M365MailMirror.Core.Database;

/// <summary>
/// Represents a database transaction for batch operations.
/// </summary>
public interface IDatabaseTransaction : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Commits the transaction, making all changes permanent.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the transaction, discarding all changes.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
