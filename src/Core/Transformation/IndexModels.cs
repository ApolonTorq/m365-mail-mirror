namespace M365MailMirror.Core.Transform;

/// <summary>
/// Options for index generation operations.
/// </summary>
public class IndexGenerationOptions
{
    /// <summary>
    /// Whether to generate HTML index files.
    /// </summary>
    public bool GenerateHtmlIndexes { get; init; } = true;
}

/// <summary>
/// Result of index generation operation.
/// </summary>
public class IndexGenerationResult
{
    /// <summary>
    /// Number of HTML index files generated.
    /// </summary>
    public int HtmlIndexesGenerated { get; init; }

    /// <summary>
    /// Number of Markdown index files generated.
    /// </summary>
    public int MarkdownIndexesGenerated { get; init; }

    /// <summary>
    /// Number of errors encountered during generation.
    /// </summary>
    public int Errors { get; init; }

    /// <summary>
    /// Time elapsed during index generation.
    /// </summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>
    /// Whether the operation completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static IndexGenerationResult Successful(int htmlCount, int errors, TimeSpan elapsed)
    {
        return new IndexGenerationResult
        {
            HtmlIndexesGenerated = htmlCount,
            MarkdownIndexesGenerated = 0,
            Errors = errors,
            Elapsed = elapsed,
            Success = true
        };
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static IndexGenerationResult Failed(string errorMessage, TimeSpan elapsed)
    {
        return new IndexGenerationResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            Elapsed = elapsed
        };
    }
}

/// <summary>
/// Summary information for a message to display in index files.
/// </summary>
public class MessageSummary
{
    /// <summary>
    /// The message subject.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// The sender's email address or display name.
    /// </summary>
    public required string Sender { get; init; }

    /// <summary>
    /// When the message was received.
    /// </summary>
    public required DateTimeOffset ReceivedTime { get; init; }

    /// <summary>
    /// Whether the message has attachments.
    /// </summary>
    public required bool HasAttachments { get; init; }

    /// <summary>
    /// Relative path to the HTML file from the index file.
    /// </summary>
    public required string HtmlFilename { get; init; }

    /// <summary>
    /// Relative path to the Markdown file from the index file.
    /// </summary>
    public required string MarkdownFilename { get; init; }
}

/// <summary>
/// Represents a node in the folder hierarchy for index generation.
/// </summary>
public class IndexNode
{
    /// <summary>
    /// Display name for this node (folder name, year, or month name).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Full path from archive root (e.g., "Inbox/2024/01").
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// The type of node in the hierarchy.
    /// </summary>
    public required IndexNodeType NodeType { get; init; }

    /// <summary>
    /// Child nodes (subfolders, years, or months).
    /// </summary>
    public List<IndexNode> Children { get; init; } = [];

    /// <summary>
    /// Messages in this node (only populated for month-level nodes).
    /// </summary>
    public List<MessageSummary> Messages { get; init; } = [];

    /// <summary>
    /// Total message count in this node and all descendants.
    /// </summary>
    public int TotalMessageCount { get; set; }
}

/// <summary>
/// Type of node in the index hierarchy.
/// </summary>
public enum IndexNodeType
{
    /// <summary>
    /// Root node (archive root).
    /// </summary>
    Root,

    /// <summary>
    /// Mail folder (e.g., Inbox, Sent Items).
    /// </summary>
    MailFolder,

    /// <summary>
    /// Year container.
    /// </summary>
    Year,

    /// <summary>
    /// Month container (contains actual messages).
    /// </summary>
    Month
}
