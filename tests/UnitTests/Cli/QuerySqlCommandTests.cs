using CliFx.Exceptions;
using CliFx.Infrastructure;
using M365MailMirror.Cli.Commands;
using Xunit;
using FluentAssertions;

namespace M365MailMirror.UnitTests.Cli;

public class QuerySqlCommandTests
{
    [Fact]
    public async Task ExecuteAsync_WithMissingArchive_ThrowsError()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new QuerySqlCommand
        {
            Query = "SELECT * FROM messages",
            ArchivePath = "/nonexistent/path"
        };

        // Act & Assert
        await Assert.ThrowsAsync<CommandException>(async () => await command.ExecuteAsync(console));

        var error = console.ReadErrorString();
        error.Should().Contain("Error");
    }

    [Fact]
    public async Task ExecuteAsync_WithoutQueryOrFile_ThrowsError()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new QuerySqlCommand
        {
            ArchivePath = "/some/path"
        };

        // Act & Assert
        await Assert.ThrowsAsync<CommandException>(async () => await command.ExecuteAsync(console));

        var error = console.ReadErrorString();
        error.Should().Contain("Error");
    }

    [Fact]
    public async Task ExecuteAsync_WithBothQueryAndFile_ThrowsError()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new QuerySqlCommand
        {
            Query = "SELECT * FROM messages",
            FilePath = "query.sql",
            ArchivePath = "/some/path"
        };

        // Act & Assert
        await Assert.ThrowsAsync<CommandException>(async () => await command.ExecuteAsync(console));

        var error = console.ReadErrorString();
        error.Should().Contain("Error");
    }

    [Fact]
    public async Task ExecuteAsync_WithInsertQuery_EnforcesReadOnly()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new QuerySqlCommand
        {
            Query = "INSERT INTO messages (graph_id) VALUES ('test')",
            ReadOnly = true,
            ArchivePath = "/some/path"
        };

        // Act & Assert
        await Assert.ThrowsAsync<CommandException>(async () => await command.ExecuteAsync(console));

        var error = console.ReadErrorString();
        error.Should().Contain("write operations");
    }

    [Fact]
    public async Task ExecuteAsync_WithUpdateQuery_EnforcesReadOnly()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new QuerySqlCommand
        {
            Query = "UPDATE messages SET subject = 'new'",
            ReadOnly = true,
            ArchivePath = "/some/path"
        };

        // Act & Assert
        await Assert.ThrowsAsync<CommandException>(async () => await command.ExecuteAsync(console));

        var error = console.ReadErrorString();
        error.Should().Contain("write operations");
    }

    [Fact]
    public async Task ExecuteAsync_WithDeleteQuery_EnforcesReadOnly()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new QuerySqlCommand
        {
            Query = "DELETE FROM messages",
            ReadOnly = true,
            ArchivePath = "/some/path"
        };

        // Act & Assert
        await Assert.ThrowsAsync<CommandException>(async () => await command.ExecuteAsync(console));

        var error = console.ReadErrorString();
        error.Should().Contain("write operations");
    }

    [Fact]
    public async Task ExecuteAsync_WithDropQuery_EnforcesReadOnly()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new QuerySqlCommand
        {
            Query = "DROP TABLE messages",
            ReadOnly = true,
            ArchivePath = "/some/path"
        };

        // Act & Assert
        await Assert.ThrowsAsync<CommandException>(async () => await command.ExecuteAsync(console));

        var error = console.ReadErrorString();
        error.Should().Contain("write operations");
    }

    [Fact]
    public async Task ExecuteAsync_WithSelectQuery_AllowedWhenReadOnly()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new QuerySqlCommand
        {
            Query = "SELECT * FROM messages",
            ReadOnly = true,
            ArchivePath = "/some/path"
        };

        // Act & Assert - Should not throw read-only error before trying to open database
        // (will fail on missing database, but not on read-only check)
        await Assert.ThrowsAsync<CommandException>(async () => await command.ExecuteAsync(console));

        var error = console.ReadErrorString();
        error.Should().Contain("Error");
        error.Should().NotContain("write operations");
    }

    [Fact]
    public async Task ExecuteAsync_WithCommentedInsertQuery_EnforcesReadOnly()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new QuerySqlCommand
        {
            Query = "-- this is a comment\nINSERT INTO messages (graph_id) VALUES ('test')",
            ReadOnly = true,
            ArchivePath = "/some/path"
        };

        // Act & Assert
        await Assert.ThrowsAsync<CommandException>(async () => await command.ExecuteAsync(console));

        var error = console.ReadErrorString();
        error.Should().Contain("write operations");
    }

    [Fact]
    public async Task ExecuteAsync_WithBlockCommentedInsertQuery_EnforcesReadOnly()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new QuerySqlCommand
        {
            Query = "/* comment */ INSERT INTO messages (graph_id) VALUES ('test')",
            ReadOnly = true,
            ArchivePath = "/some/path"
        };

        // Act & Assert
        await Assert.ThrowsAsync<CommandException>(async () => await command.ExecuteAsync(console));

        var error = console.ReadErrorString();
        error.Should().Contain("write operations");
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistentFile_ThrowsError()
    {
        // Arrange
        using var console = new FakeInMemoryConsole();
        var command = new QuerySqlCommand
        {
            FilePath = "/nonexistent/query.sql",
            ArchivePath = "/some/path"
        };

        // Act & Assert
        await Assert.ThrowsAsync<CommandException>(async () => await command.ExecuteAsync(console));

        var error = console.ReadErrorString();
        error.Should().Contain("Error");
    }

    [Fact]
    public void IsReadOnlyQuery_WithSelectQuery_ReturnsTrue()
    {
        // Test that SELECT queries are recognized as read-only
        var result = QuerySqlCommandHelper.IsReadOnlyQueryPublic("SELECT * FROM messages");
        result.Should().BeTrue();
    }

    [Fact]
    public void IsReadOnlyQuery_WithWithQuery_ReturnsTrue()
    {
        // Test that WITH queries (CTEs) are recognized as read-only
        var result = QuerySqlCommandHelper.IsReadOnlyQueryPublic("WITH cte AS (SELECT * FROM messages) SELECT * FROM cte");
        result.Should().BeTrue();
    }

    [Fact]
    public void IsReadOnlyQuery_WithExplainQuery_ReturnsTrue()
    {
        // Test that EXPLAIN queries are recognized as read-only
        var result = QuerySqlCommandHelper.IsReadOnlyQueryPublic("EXPLAIN SELECT * FROM messages");
        result.Should().BeTrue();
    }

    [Fact]
    public void IsReadOnlyQuery_WithInsertQuery_ReturnsFalse()
    {
        // Test that INSERT queries are recognized as write operations
        var result = QuerySqlCommandHelper.IsReadOnlyQueryPublic("INSERT INTO messages (graph_id) VALUES ('test')");
        result.Should().BeFalse();
    }

    [Fact]
    public void IsReadOnlyQuery_WithUpdateQuery_ReturnsFalse()
    {
        // Test that UPDATE queries are recognized as write operations
        var result = QuerySqlCommandHelper.IsReadOnlyQueryPublic("UPDATE messages SET subject = 'new'");
        result.Should().BeFalse();
    }

    [Fact]
    public void IsReadOnlyQuery_WithDeleteQuery_ReturnsFalse()
    {
        // Test that DELETE queries are recognized as write operations
        var result = QuerySqlCommandHelper.IsReadOnlyQueryPublic("DELETE FROM messages");
        result.Should().BeFalse();
    }

    [Fact]
    public void IsReadOnlyQuery_WithCommentedInsertQuery_ReturnsFalse()
    {
        // Test that commented-out INSERT queries are still recognized as write operations
        var result = QuerySqlCommandHelper.IsReadOnlyQueryPublic("-- comment\nINSERT INTO messages (graph_id) VALUES ('test')");
        result.Should().BeFalse();
    }

    [Fact]
    public void IsReadOnlyQuery_WithBlockCommentedInsertQuery_ReturnsFalse()
    {
        // Test that block-commented INSERT queries are still recognized as write operations
        var result = QuerySqlCommandHelper.IsReadOnlyQueryPublic("/* comment */ INSERT INTO messages (graph_id) VALUES ('test')");
        result.Should().BeFalse();
    }
}

/// <summary>
/// Helper class to expose private methods for testing
/// </summary>
public static class QuerySqlCommandHelper
{
    public static bool IsReadOnlyQueryPublic(string sql)
    {
        // This uses reflection to access the private method
        var method = typeof(QuerySqlCommand).GetMethod("IsReadOnlyQuery",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        if (method == null)
        {
            throw new InvalidOperationException("IsReadOnlyQuery method not found");
        }

        var result = method.Invoke(null, new object[] { sql });
        return (bool)(result ?? false);
    }
}
