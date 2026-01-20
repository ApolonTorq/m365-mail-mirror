using M365MailMirror.Core.Database;
using Microsoft.Data.Sqlite;

namespace M365MailMirror.Infrastructure.Database;

/// <summary>
/// SQLite implementation of database transaction.
/// </summary>
internal sealed class SqliteDatabaseTransaction : IDatabaseTransaction
{
    private readonly SqliteTransaction _transaction;
    private bool _disposed;

    public SqliteDatabaseTransaction(SqliteTransaction transaction)
    {
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _transaction.Commit();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _transaction.Rollback();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _transaction.Dispose();
            _disposed = true;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _transaction.Dispose();
            _disposed = true;
        }
        return ValueTask.CompletedTask;
    }
}
