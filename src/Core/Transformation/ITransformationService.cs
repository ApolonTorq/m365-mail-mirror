using M365MailMirror.Core.Database.Entities;

namespace M365MailMirror.Core.Transform;

/// <summary>
/// Options for transformation operations.
/// </summary>
public class TransformOptions
{
    /// <summary>
    /// Only transform using this type (html, markdown, or attachments).
    /// If null, run all enabled transformations.
    /// </summary>
    public string? Only { get; init; }

    /// <summary>
    /// Force regeneration even if already transformed.
    /// </summary>
    public bool Force { get; init; }

    /// <summary>
    /// Number of parallel transformations.
    /// </summary>
    public int MaxParallel { get; init; } = 5;

    /// <summary>
    /// Enable HTML transformation.
    /// </summary>
    public bool EnableHtml { get; init; } = true;

    /// <summary>
    /// Enable Markdown transformation.
    /// </summary>
    public bool EnableMarkdown { get; init; } = true;

    /// <summary>
    /// Enable attachment extraction.
    /// </summary>
    public bool EnableAttachments { get; init; } = true;

    /// <summary>
    /// HTML-specific transformation options.
    /// </summary>
    public HtmlTransformOptions? HtmlOptions { get; init; }

    /// <summary>
    /// Attachment extraction options.
    /// </summary>
    public AttachmentExtractOptions? AttachmentOptions { get; init; }
}

/// <summary>
/// Options for inline transformation during sync.
/// Used when transforming messages immediately after download.
/// </summary>
public class InlineTransformOptions
{
    /// <summary>
    /// Whether to generate HTML transformation.
    /// </summary>
    public bool GenerateHtml { get; init; }

    /// <summary>
    /// Whether to generate Markdown transformation.
    /// </summary>
    public bool GenerateMarkdown { get; init; }

    /// <summary>
    /// Whether to extract attachments.
    /// </summary>
    public bool ExtractAttachments { get; init; }

    /// <summary>
    /// HTML-specific transformation options.
    /// </summary>
    public HtmlTransformOptions? HtmlOptions { get; init; }

    /// <summary>
    /// Attachment extraction options.
    /// </summary>
    public AttachmentExtractOptions? AttachmentOptions { get; init; }

    /// <summary>
    /// Returns true if any transformation is enabled.
    /// </summary>
    public bool HasAnyTransformation => GenerateHtml || GenerateMarkdown || ExtractAttachments;
}

/// <summary>
/// HTML-specific transformation options.
/// </summary>
public class HtmlTransformOptions
{
    /// <summary>
    /// Whether to embed CSS styles inline in each HTML file.
    /// When false, styles are included in a style block in the head.
    /// </summary>
    public bool InlineStyles { get; init; }

    /// <summary>
    /// Whether to strip external image references from HTML.
    /// When true, removes img tags with http/https sources for privacy.
    /// </summary>
    public bool StripExternalImages { get; init; }

    /// <summary>
    /// Whether to hide CC recipients in HTML/Markdown output.
    /// </summary>
    public bool HideCc { get; init; }

    /// <summary>
    /// Whether to hide BCC recipients in HTML/Markdown output.
    /// Defaults to true since BCC is rarely stored in received messages.
    /// </summary>
    public bool HideBcc { get; init; } = true;
}

/// <summary>
/// Attachment extraction options.
/// </summary>
public class AttachmentExtractOptions
{
    /// <summary>
    /// Whether to skip extraction of executable files for security.
    /// Defaults to true.
    /// </summary>
    public bool SkipExecutables { get; init; } = true;
}

/// <summary>
/// Result of a transformation operation.
/// </summary>
public class TransformResult
{
    /// <summary>
    /// Whether the transformation completed successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Number of messages transformed.
    /// </summary>
    public required int MessagesTransformed { get; init; }

    /// <summary>
    /// Number of messages skipped (already transformed).
    /// </summary>
    public required int MessagesSkipped { get; init; }

    /// <summary>
    /// Number of errors encountered.
    /// </summary>
    public required int Errors { get; init; }

    /// <summary>
    /// Error message if not successful.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Elapsed time for the transformation.
    /// </summary>
    public required TimeSpan Elapsed { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static TransformResult Successful(int transformed, int skipped, int errors, TimeSpan elapsed)
    {
        return new TransformResult
        {
            Success = true,
            MessagesTransformed = transformed,
            MessagesSkipped = skipped,
            Errors = errors,
            Elapsed = elapsed
        };
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static TransformResult Failed(string errorMessage, TimeSpan elapsed)
    {
        return new TransformResult
        {
            Success = false,
            MessagesTransformed = 0,
            MessagesSkipped = 0,
            Errors = 0,
            ErrorMessage = errorMessage,
            Elapsed = elapsed
        };
    }
}

/// <summary>
/// Progress information for transformation operations.
/// </summary>
public class TransformProgress
{
    /// <summary>
    /// The current phase of transformation.
    /// </summary>
    public string Phase { get; init; } = "";

    /// <summary>
    /// Current transformation type being processed.
    /// </summary>
    public string? TransformationType { get; init; }

    /// <summary>
    /// Total messages to transform.
    /// </summary>
    public int TotalMessages { get; init; }

    /// <summary>
    /// Messages processed so far.
    /// </summary>
    public int ProcessedMessages { get; init; }

    /// <summary>
    /// Total transformations completed.
    /// </summary>
    public int TotalTransformed { get; init; }
}

/// <summary>
/// Progress callback delegate for transformation operations.
/// </summary>
public delegate void TransformProgressCallback(TransformProgress progress);

/// <summary>
/// Service for transforming EML files to other formats.
/// </summary>
public interface ITransformationService
{
    /// <summary>
    /// Transforms EML files to the specified output formats.
    /// </summary>
    /// <param name="options">Transformation options.</param>
    /// <param name="progressCallback">Optional progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the transformation operation.</returns>
    Task<TransformResult> TransformAsync(
        TransformOptions options,
        TransformProgressCallback? progressCallback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transforms a single message immediately after download.
    /// Used for inline transformation during sync.
    /// </summary>
    /// <param name="message">The message entity that was just stored.</param>
    /// <param name="options">Which transformations to apply (HTML, Markdown, attachments).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if transformation succeeded, false if any errors occurred.</returns>
    Task<bool> TransformSingleMessageAsync(
        Message message,
        InlineTransformOptions options,
        CancellationToken cancellationToken = default);
}
