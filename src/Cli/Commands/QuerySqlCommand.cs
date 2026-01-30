using System.Diagnostics;
using System.Globalization;
using System.Text;
using CliFx.Attributes;
using CliFx.Infrastructure;
using M365MailMirror.Core.Configuration;
using M365MailMirror.Core.Exceptions;
using M365MailMirror.Core.Logging;
using M365MailMirror.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace M365MailMirror.Cli.Commands;

/// <summary>
/// Executes SQL queries against the SQLite metadata database and returns results in various formats.
/// </summary>
[Command("query-sql", Description = "Execute SQL queries against the metadata database")]
public class QuerySqlCommand : BaseCommand
{
    private static readonly char[] LineBreakChars = { '\n', '\r' };
    private static readonly char[] SqlSeparatorChars = { ' ', '\t', '\n', '\r', ';' };

    [CommandParameter(0, Name = "query", Description = "SQL query to execute")]
    public string? Query { get; init; }

    [CommandOption("file", 'f', Description = "Path to file containing SQL query")]
    public string? FilePath { get; init; }

    [CommandOption("format", Description = "Output format: markdown, json, csv")]
    public OutputFormat Format { get; init; } = OutputFormat.Markdown;

    [CommandOption("limit", Description = "Maximum rows to return (default: 10000)")]
    public int Limit { get; init; } = 10000;

    [CommandOption("read-only", Description = "Enforce read-only mode (default: true)")]
    public bool ReadOnly { get; init; } = true;

    [CommandOption("timeout", Description = "Query timeout in seconds (default: 120)")]
    public int Timeout { get; init; } = 120;

    [CommandOption("config", 'c', Description = "Path to configuration file (searches ./config.yaml, then ~/.config/m365-mail-mirror/config.yaml)")]
    public string? ConfigPath { get; init; }

    [CommandOption("archive", 'a', Description = "Path to the mail archive (defaults to config OutputPath, which defaults to current directory)")]
    public string? ArchivePath { get; init; }

    [CommandOption("verbose", 'v', Description = "Enable verbose logging")]
    public bool Verbose { get; init; }

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        ConfigureLogging(console, Verbose);
        var logger = LoggerFactory.CreateLogger<QuerySqlCommand>();
        var cancellationToken = console.RegisterCancellationHandler();

        // Validate parameters
        if (string.IsNullOrWhiteSpace(Query) && string.IsNullOrWhiteSpace(FilePath))
        {
            throw new M365MailMirrorException(
                "Either provide a query as an argument or use --file to specify a query file",
                CliExitCodes.GeneralError);
        }

        if (!string.IsNullOrWhiteSpace(Query) && !string.IsNullOrWhiteSpace(FilePath))
        {
            throw new M365MailMirrorException(
                "Specify either a query argument or --file, not both",
                CliExitCodes.GeneralError);
        }

        // Read query first (before checking archive)
        var queryText = Query;
        if (!string.IsNullOrWhiteSpace(FilePath))
        {
            if (!File.Exists(FilePath))
            {
                throw new M365MailMirrorException(
                    $"Query file not found: {FilePath}",
                    CliExitCodes.FileSystemError);
            }

            queryText = await File.ReadAllTextAsync(FilePath, cancellationToken);
        }

        queryText = queryText?.Trim() ?? throw new M365MailMirrorException("Query cannot be empty", CliExitCodes.GeneralError);

        // Enforce read-only mode (check this BEFORE verifying archive exists, for security)
        if (ReadOnly && !IsReadOnlyQuery(queryText))
        {
            throw new M365MailMirrorException(
                "Query contains write operations (INSERT/UPDATE/DELETE/DROP/ALTER).\n" +
                "Use --read-only false to allow modifications (dangerous).",
                CliExitCodes.GeneralError);
        }

        // Load configuration
        var config = ConfigurationLoader.Load(ConfigPath);
        var archiveRoot = ArchivePath ?? config.OutputPath;

        // Verify archive directory exists
        if (!Directory.Exists(archiveRoot))
        {
            throw new M365MailMirrorException(
                $"Archive directory does not exist: {archiveRoot}",
                CliExitCodes.FileSystemError);
        }

        // Check if database exists
        var databasePath = Path.Combine(archiveRoot, StateDatabase.DatabaseDirectory, StateDatabase.DefaultDatabaseFilename);
        if (!File.Exists(databasePath))
        {
            throw new M365MailMirrorException(
                $"Database not found at: {databasePath}\nRun 'sync' to initialize the archive.",
                CliExitCodes.FileSystemError);
        }

        // Apply LIMIT if not already present
        if (Limit > 0)
        {
            queryText = ApplyLimit(queryText, Limit);
        }

        // Execute query
        var stopwatch = Stopwatch.StartNew();
        var formatter = CreateFormatter();

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = ReadOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
                Cache = SqliteCacheMode.Private
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = queryText;
            command.CommandTimeout = Timeout;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Timeout));

            try
            {
                using var reader = await command.ExecuteReaderAsync(cts.Token);
                var rowCount = await formatter.FormatAsync(reader, console.Output, Limit, cancellationToken);
                stopwatch.Stop();

                if (rowCount == 0)
                {
                    await console.Output.WriteLineAsync("No rows returned.");
                }
                else
                {
                    await console.Output.WriteLineAsync($"\n{rowCount} rows returned in {stopwatch.Elapsed.TotalSeconds:F2}s");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new M365MailMirrorException(
                    $"Query timed out after {Timeout}s. Use --timeout <seconds> to increase limit.",
                    CliExitCodes.GeneralError);
            }
        }
        catch (SqliteException ex)
        {
            throw new M365MailMirrorException(
                $"SQL error: {ex.Message}",
                CliExitCodes.GeneralError);
        }
    }

    /// <summary>
    /// Checks if a query is read-only (SELECT, WITH, or EXPLAIN).
    /// Strips leading comments before checking the first keyword.
    /// </summary>
    private static bool IsReadOnlyQuery(string sql)
    {
        var trimmed = sql.TrimStart();

        // Remove leading comments
        while (trimmed.Length > 0)
        {
            if (trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                // Skip line comment
                var newlineIdx = trimmed.IndexOfAny(LineBreakChars);
                trimmed = newlineIdx >= 0 ? trimmed[(newlineIdx + 1)..].TrimStart() : string.Empty;
            }
            else if (trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                // Skip block comment
                var endIdx = trimmed.IndexOf("*/", StringComparison.Ordinal);
                if (endIdx >= 0)
                {
                    trimmed = trimmed[(endIdx + 2)..].TrimStart();
                }
                else
                {
                    trimmed = string.Empty;
                }
            }
            else
            {
                break;
            }
        }

        // Get first meaningful token
        var tokens = trimmed.Split(SqlSeparatorChars, StringSplitOptions.RemoveEmptyEntries);
        var firstToken = tokens.FirstOrDefault()?.ToUpperInvariant();

        // Allow SELECT, WITH (for CTEs), and EXPLAIN
        return firstToken is "SELECT" or "WITH" or "EXPLAIN";
    }

    /// <summary>
    /// Applies LIMIT to the query if not already present.
    /// </summary>
    private static string ApplyLimit(string sql, int limit)
    {
        if (limit <= 0)
        {
            return sql;
        }

        // Check if query already has LIMIT (case-insensitive)
        if (sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase))
        {
            return sql;
        }

        // Append LIMIT clause
        return $"{sql.TrimEnd(';')} LIMIT {limit}";
    }

    /// <summary>
    /// Creates the appropriate output formatter based on the Format setting.
    /// </summary>
    private IOutputFormatter CreateFormatter()
    {
        return Format switch
        {
            OutputFormat.Markdown => new MarkdownTableFormatter(),
            OutputFormat.Json => new JsonFormatter(),
            OutputFormat.Csv => new CsvFormatter(),
            _ => throw new M365MailMirrorException($"Unknown output format: {Format}", CliExitCodes.GeneralError)
        };
    }
}

/// <summary>
/// Output format for query results.
/// </summary>
public enum OutputFormat
{
    Markdown,
    Json,
    Csv
}

/// <summary>
/// Interface for formatting query results.
/// </summary>
public interface IOutputFormatter
{
    /// <summary>
    /// Formats the query results from a data reader and writes to output.
    /// </summary>
    /// <param name="reader">The SQLite data reader with query results</param>
    /// <param name="output">The output text writer</param>
    /// <param name="limit">Maximum number of rows to write</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of rows written</returns>
    Task<int> FormatAsync(SqliteDataReader reader, TextWriter output, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// Formats query results as GitHub-flavored Markdown tables.
/// </summary>
public class MarkdownTableFormatter : IOutputFormatter
{
    public async Task<int> FormatAsync(SqliteDataReader reader, TextWriter output, int limit, CancellationToken cancellationToken)
    {
        // Buffer all results to calculate column widths
        var columns = new List<string>();
        var rows = new List<Dictionary<string, string>>();

        // Get column names
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }

        // Read all rows (up to limit)
        var rowCount = 0;
        while (await reader.ReadAsync(cancellationToken) && (limit <= 0 || rowCount < limit))
        {
            var row = new Dictionary<string, string>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var value = FormatCellValue(reader.GetValue(i));
                row[columns[i]] = value;
            }

            rows.Add(row);
            rowCount++;
        }

        if (rowCount == 0)
        {
            return 0;
        }

        // Calculate column widths
        var widths = CalculateColumnWidths(columns, rows);

        // Write header
        var headerCells = columns.Select(c => c.PadRight(widths[c])).ToList();
        await output.WriteLineAsync("| " + string.Join(" | ", headerCells) + " |");

        // Write separator
        var separatorCells = columns.Select(c => new string('-', widths[c])).ToList();
        await output.WriteLineAsync("| " + string.Join(" | ", separatorCells) + " |");

        // Write rows
        foreach (var row in rows)
        {
            var cells = columns.Select(c => row[c].PadRight(widths[c])).ToList();
            await output.WriteLineAsync("| " + string.Join(" | ", cells) + " |");
        }

        return rowCount;
    }

    private static string FormatCellValue(object? value)
    {
        if (value == null || value == DBNull.Value)
        {
            return "(null)";
        }

        var str = value.ToString() ?? string.Empty;

        // Truncate long strings at 500 characters
        if (str.Length > 500)
        {
            return str[..497] + "...";
        }

        // Escape pipe characters in cell values
        return str.Replace("|", "\\|");
    }

    private static Dictionary<string, int> CalculateColumnWidths(List<string> columns, List<Dictionary<string, string>> rows)
    {
        var widths = columns.ToDictionary(c => c, c => Math.Min(c.Length, 500));

        foreach (var row in rows)
        {
            foreach (var col in columns)
            {
                var cellLength = Math.Min(row[col].Length, 500);
                widths[col] = Math.Max(widths[col], cellLength);
            }
        }

        return widths;
    }
}

/// <summary>
/// Formats query results as JSON (array of objects).
/// </summary>
public class JsonFormatter : IOutputFormatter
{
    public async Task<int> FormatAsync(SqliteDataReader reader, TextWriter output, int limit, CancellationToken cancellationToken)
    {
        await output.WriteAsync("[");

        var rowCount = 0;
        var isFirstRow = true;

        while (await reader.ReadAsync(cancellationToken) && (limit <= 0 || rowCount < limit))
        {
            if (!isFirstRow)
            {
                await output.WriteAsync(",");
            }

            await output.WriteLineAsync();
            await output.WriteAsync("  {");

            var isFirstColumn = true;
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (!isFirstColumn)
                {
                    await output.WriteAsync(",");
                }

                await output.WriteLineAsync();
                var columnName = reader.GetName(i);
                var value = reader.GetValue(i);
                var jsonValue = FormatJsonValue(value);

                await output.WriteAsync($"    \"{EscapeJsonString(columnName)}\": {jsonValue}");
                isFirstColumn = false;
            }

            await output.WriteLineAsync();
            await output.WriteAsync("  }");

            isFirstRow = false;
            rowCount++;
        }

        if (rowCount > 0)
        {
            await output.WriteLineAsync();
        }

        await output.WriteLineAsync("]");

        return rowCount;
    }

    private static string FormatJsonValue(object? value)
    {
        if (value == null || value == DBNull.Value)
        {
            return "null";
        }

        return value switch
        {
            bool b => b ? "true" : "false",
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            string s => $"\"{EscapeJsonString(s)}\"",
            _ => $"\"{EscapeJsonString(value.ToString() ?? string.Empty)}\""
        };
    }

    private static string EscapeJsonString(string str)
    {
        return str
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}

/// <summary>
/// Formats query results as RFC 4180 compliant CSV.
/// </summary>
public class CsvFormatter : IOutputFormatter
{
    public async Task<int> FormatAsync(SqliteDataReader reader, TextWriter output, int limit, CancellationToken cancellationToken)
    {
        // Write header row
        var headerRow = new List<string>();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            headerRow.Add(EscapeCsvField(reader.GetName(i)));
        }

        await output.WriteLineAsync(string.Join(",", headerRow));

        // Write data rows
        var rowCount = 0;
        while (await reader.ReadAsync(cancellationToken) && (limit <= 0 || rowCount < limit))
        {
            var row = new List<string>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                row.Add(EscapeCsvField(value));
            }

            await output.WriteLineAsync(string.Join(",", row));
            rowCount++;
        }

        return rowCount;
    }

    private static string EscapeCsvField(object? value)
    {
        if (value == null || value == DBNull.Value)
        {
            return string.Empty;
        }

        var str = value.ToString() ?? string.Empty;

        // Quote field if it contains comma, quote, or newline
        if (str.Contains(',') || str.Contains('"') || str.Contains('\n') || str.Contains('\r'))
        {
            // Escape quotes by doubling them per RFC 4180
            str = str.Replace("\"", "\"\"");
            return $"\"{str}\"";
        }

        return str;
    }
}
