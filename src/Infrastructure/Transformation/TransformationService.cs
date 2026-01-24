using System.Diagnostics;
using System.Globalization;
using M365MailMirror.Core.Configuration;
using M365MailMirror.Core.Database;
using M365MailMirror.Core.Database.Entities;
using M365MailMirror.Core.Logging;
using M365MailMirror.Core.Security;
using M365MailMirror.Core.Storage;
using M365MailMirror.Core.Transform;
using MimeKit;

namespace M365MailMirror.Infrastructure.Transform;

/// <summary>
/// Service for transforming EML files to HTML, Markdown, and extracting attachments.
/// </summary>
public class TransformationService : ITransformationService
{
    private readonly IStateDatabase _database;
    private readonly IEmlStorageService _emlStorage;
    private readonly string _archiveRoot;
    private readonly IAppLogger _logger;
    private readonly ZipExtractor _zipExtractor;

    /// <summary>
    /// Current configuration version for transformations.
    /// Change this when transformation logic changes.
    /// v2: Added breadcrumb navigation to HTML and Markdown outputs.
    /// v3: Unified transformed/ directory, inline images to images/ folder, cid: rewriting.
    /// v4: Added optional "View in Outlook" link.
    /// </summary>
    public const string CurrentConfigVersion = "v4";

    /// <summary>
    /// Creates a new TransformationService.
    /// </summary>
    public TransformationService(
        IStateDatabase database,
        IEmlStorageService emlStorage,
        string archiveRoot,
        ZipExtractionConfiguration? zipConfig = null,
        IAppLogger? logger = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _emlStorage = emlStorage ?? throw new ArgumentNullException(nameof(emlStorage));
        _archiveRoot = archiveRoot ?? throw new ArgumentNullException(nameof(archiveRoot));
        _logger = logger ?? LoggerFactory.CreateLogger<TransformationService>();
        _zipExtractor = new ZipExtractor(zipConfig, _logger);
    }

    /// <inheritdoc />
    public async Task<TransformResult> TransformAsync(
        TransformOptions options,
        TransformProgressCallback? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var transformed = 0;
        var skipped = 0;
        var errors = 0;

        try
        {
            // Determine which transformation types to run
            var transformTypes = GetTransformationTypes(options);

            if (transformTypes.Count == 0)
            {
                _logger.Warning("No transformations enabled");
                stopwatch.Stop();
                return TransformResult.Successful(0, 0, 0, stopwatch.Elapsed);
            }

            // Process each transformation type
            foreach (var transformType in transformTypes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progressCallback?.Invoke(new TransformProgress
                {
                    Phase = "Finding messages to transform",
                    TransformationType = transformType
                });

                // Get messages needing this transformation
                IReadOnlyList<Message> messages;
                if (options.Force)
                {
                    // Force mode: get all non-quarantined messages
                    messages = await GetAllMessagesAsync(cancellationToken);
                }
                else
                {
                    // Normal mode: only messages needing transformation
                    messages = await _database.GetMessagesNeedingTransformationAsync(
                        transformType,
                        CurrentConfigVersion,
                        cancellationToken);
                }

                _logger.Debug("{0}: {1} messages to process", transformType, messages.Count);

                if (messages.Count == 0)
                {
                    continue;
                }

                // Process messages in parallel
                using var semaphore = new SemaphoreSlim(options.MaxParallel);
                var processedCount = 0;

                var tasks = messages.Select(async message =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        var result = await TransformMessageAsync(message, transformType, options.HtmlOptions, options.AttachmentOptions, cancellationToken);
                        Interlocked.Increment(ref processedCount);

                        progressCallback?.Invoke(new TransformProgress
                        {
                            Phase = "Transforming",
                            TransformationType = transformType,
                            TotalMessages = messages.Count,
                            ProcessedMessages = processedCount,
                            TotalTransformed = transformed
                        });

                        return result;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                var results = await Task.WhenAll(tasks);

                foreach (var result in results)
                {
                    switch (result)
                    {
                        case TransformMessageResult.Transformed:
                            transformed++;
                            break;
                        case TransformMessageResult.Skipped:
                            skipped++;
                            break;
                        case TransformMessageResult.Error:
                            errors++;
                            break;
                    }
                }
            }

            stopwatch.Stop();
            return TransformResult.Successful(transformed, skipped, errors, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.Warning("Transformation cancelled after {0}", stopwatch.Elapsed);
            return TransformResult.Failed("Transformation was cancelled", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.Error(ex, "Transformation failed: {0}", ex.Message);
            return TransformResult.Failed(ex.Message, stopwatch.Elapsed);
        }
    }

    private static List<string> GetTransformationTypes(TransformOptions options)
    {
        var types = new List<string>();

        // If "only" is specified, only run that type
        if (!string.IsNullOrEmpty(options.Only))
        {
            var only = options.Only.ToLowerInvariant();
            if (only == "html" || only == "markdown" || only == "attachments")
            {
                types.Add(only);
            }
            return types;
        }

        // Attachments must be extracted BEFORE html/markdown so that
        // attachment links and sizes are available when generating output
        if (options.EnableAttachments)
            types.Add("attachments");
        if (options.EnableHtml)
            types.Add("html");
        if (options.EnableMarkdown)
            types.Add("markdown");

        return types;
    }

    private async Task<IReadOnlyList<Message>> GetAllMessagesAsync(CancellationToken cancellationToken)
    {
        var allMessages = new List<Message>();
        var folders = await _database.GetAllFoldersAsync(cancellationToken);

        foreach (var folder in folders)
        {
            var messages = await _database.GetMessagesByFolderAsync(folder.LocalPath, cancellationToken);
            allMessages.AddRange(messages);
        }

        return allMessages;
    }

    private async Task<TransformMessageResult> TransformMessageAsync(
        Message message,
        string transformType,
        HtmlTransformOptions? htmlOptions,
        AttachmentExtractOptions? attachmentOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if EML file exists
            if (!_emlStorage.Exists(message.LocalPath))
            {
                _logger.Warning("EML file not found: {0}", message.LocalPath);
                return TransformMessageResult.Error;
            }

            // Load the MIME message
            MimeMessage mimeMessage;
            using (var stream = _emlStorage.OpenRead(message.LocalPath))
            {
                mimeMessage = await MimeMessage.LoadAsync(stream, cancellationToken);
            }

            // Perform the transformation
            string outputPath;
            switch (transformType)
            {
                case "html":
                    outputPath = await TransformToHtmlAsync(message, mimeMessage, htmlOptions, cancellationToken);
                    break;
                case "markdown":
                    outputPath = await TransformToMarkdownAsync(message, mimeMessage, htmlOptions, cancellationToken);
                    break;
                case "attachments":
                    outputPath = await ExtractAttachmentsAsync(message, mimeMessage, attachmentOptions, cancellationToken);
                    break;
                default:
                    _logger.Warning("Unknown transformation type: {0}", transformType);
                    return TransformMessageResult.Error;
            }

            // Record the transformation in the database
            var transformation = new Core.Database.Entities.Transformation
            {
                MessageId = message.GraphId,
                TransformationType = transformType,
                AppliedAt = DateTimeOffset.UtcNow,
                ConfigVersion = CurrentConfigVersion,
                OutputPath = outputPath
            };

            await _database.UpsertTransformationAsync(transformation, cancellationToken);

            _logger.Debug("Transformed {0} ({1}): {2}", message.Subject ?? message.GraphId, transformType, outputPath);
            return TransformMessageResult.Transformed;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error transforming message {0} ({1}): {2}", message.GraphId, transformType, ex.Message);
            return TransformMessageResult.Error;
        }
    }

    private async Task<string> TransformToHtmlAsync(Message message, MimeMessage mimeMessage, HtmlTransformOptions? htmlOptions, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Build output path: transformed/{folder}/{YYYY}/{MM}/{filename}.html
        var outputDir = BuildOutputDirectory("transformed", message.FolderPath, message.ReceivedTime);
        Directory.CreateDirectory(Path.Combine(_archiveRoot, outputDir));

        var filename = Path.GetFileNameWithoutExtension(Path.GetFileName(message.LocalPath)) + ".html";
        var outputPath = Path.Combine(outputDir, filename);
        var fullPath = Path.Combine(_archiveRoot, outputPath);

        // Fetch attachments for this message to include links in the output
        var attachments = await _database.GetAttachmentsForMessageAsync(message.GraphId, cancellationToken);

        // Generate HTML with attachment links, breadcrumb navigation, and optional Outlook link
        var html = GenerateHtml(mimeMessage, outputPath, attachments, message.FolderPath, htmlOptions, message.ImmutableId);

        await File.WriteAllTextAsync(fullPath, html, cancellationToken);

        return outputPath;
    }

    private async Task<string> TransformToMarkdownAsync(Message message, MimeMessage mimeMessage, HtmlTransformOptions? htmlOptions, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Build output path: transformed/{folder}/{YYYY}/{MM}/{filename}.md
        var outputDir = BuildOutputDirectory("transformed", message.FolderPath, message.ReceivedTime);
        Directory.CreateDirectory(Path.Combine(_archiveRoot, outputDir));

        var filename = Path.GetFileNameWithoutExtension(Path.GetFileName(message.LocalPath)) + ".md";
        var outputPath = Path.Combine(outputDir, filename);
        var fullPath = Path.Combine(_archiveRoot, outputPath);

        // Fetch attachments for this message to include links in the output
        var attachments = await _database.GetAttachmentsForMessageAsync(message.GraphId, cancellationToken);

        // Generate Markdown with attachment links, breadcrumb navigation, and optional Outlook link
        var markdown = GenerateMarkdown(mimeMessage, outputPath, attachments, message.FolderPath, htmlOptions, message.ImmutableId);

        await File.WriteAllTextAsync(fullPath, markdown, cancellationToken);

        return outputPath;
    }

    private async Task<string> ExtractAttachmentsAsync(Message message, MimeMessage mimeMessage, AttachmentExtractOptions? attachmentOptions, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Delete existing attachment records for this message to prevent duplicates on re-extraction
        await _database.DeleteAttachmentsForMessageAsync(message.GraphId, cancellationToken);

        // Build paths for the new structure:
        // - Inline images: transformed/{folder}/{YYYY}/{MM}/images/{email_filename}_{n}.{ext}
        // - Regular attachments: transformed/{folder}/{YYYY}/{MM}/attachments/{email_filename}_attachments/{filename}
        var imagesDir = BuildOutputDirectory("images", message.FolderPath, message.ReceivedTime);
        var attachmentsBaseDir = BuildOutputDirectory("attachments", message.FolderPath, message.ReceivedTime);
        var emailBaseName = Path.GetFileNameWithoutExtension(Path.GetFileName(message.LocalPath));
        var attachmentDir = Path.Combine(attachmentsBaseDir, $"{emailBaseName}_attachments");

        // Delete any existing attachment/image files to start fresh
        var fullAttachmentDir = Path.Combine(_archiveRoot, attachmentDir);
        if (Directory.Exists(fullAttachmentDir))
        {
            Directory.Delete(fullAttachmentDir, recursive: true);
        }

        // Note: We don't delete the entire images folder since it's shared across emails in the same month
        // Instead, we'll overwrite individual image files as needed

        var hasContent = false;
        var attachmentCount = 0;
        var inlineImageCount = 0;
        var attachmentDirCreated = false;
        var imagesDirCreated = false;

        // Default to skipping executables if no options provided
        var skipExecutables = attachmentOptions?.SkipExecutables ?? true;

        // Use BodyParts instead of Attachments to include inline images
        // MimeMessage.Attachments only returns parts with Content-Disposition: attachment
        // Inline images have Content-Disposition: inline and are part of BodyParts
        foreach (var bodyPart in mimeMessage.BodyParts.OfType<MimePart>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip text parts (they're the message body, not attachments/images)
            if (bodyPart is TextPart)
                continue;

            var disposition = bodyPart.ContentDisposition?.Disposition;
            var isInline = disposition == ContentDisposition.Inline;
            var isAttachment = disposition == ContentDisposition.Attachment;
            var contentId = bodyPart.ContentId; // For cid: reference mapping

            // Also treat parts with ContentId but no explicit disposition as inline images
            // (some email clients don't set Content-Disposition but do set Content-Id)
            var hasContentId = !string.IsNullOrEmpty(contentId);

            // Skip if neither inline, attachment, nor has contentId
            if (!isInline && !isAttachment && !hasContentId)
                continue;

            // If has ContentId but no disposition, treat as inline
            if (hasContentId && !isInline && !isAttachment)
                isInline = true;

            if (isInline)
            {
                // Extract inline image to images/ folder with naming: {email_filename}_{n}.{ext}
                inlineImageCount++;
                var originalFilename = bodyPart.FileName ?? $"image_{inlineImageCount}";
                var ext = Path.GetExtension(originalFilename);
                if (string.IsNullOrEmpty(ext))
                {
                    // Try to determine extension from content type
                    ext = GetExtensionFromContentType(bodyPart.ContentType?.MimeType) ?? ".bin";
                }
                var imageFilename = $"{emailBaseName}_{inlineImageCount}{ext}";

                // Create images directory if needed
                if (!imagesDirCreated)
                {
                    Directory.CreateDirectory(Path.Combine(_archiveRoot, imagesDir));
                    imagesDirCreated = true;
                }

                var imagePath = Path.Combine(imagesDir, imageFilename);
                var fullImagePath = Path.Combine(_archiveRoot, imagePath);

                // Handle filename collisions (unlikely but possible)
                var counter = 1;
                while (File.Exists(fullImagePath))
                {
                    imageFilename = $"{emailBaseName}_{inlineImageCount}_{counter}{ext}";
                    imagePath = Path.Combine(imagesDir, imageFilename);
                    fullImagePath = Path.Combine(_archiveRoot, imagePath);
                    counter++;
                }

                // Extract the image
                {
                    using var stream = File.Create(fullImagePath);
                    await bodyPart.Content.DecodeToAsync(stream, cancellationToken);
                }

                var fileSize = new FileInfo(fullImagePath).Length;
                hasContent = true;

                // Record inline image in database with ContentId for cid: mapping
                var imageEntity = new Attachment
                {
                    MessageId = message.GraphId,
                    Filename = originalFilename,
                    FilePath = imagePath,
                    SizeBytes = fileSize,
                    ContentType = bodyPart.ContentType?.MimeType ?? "image/unknown",
                    IsInline = true,
                    ContentId = contentId,
                    Skipped = false,
                    SkipReason = null,
                    ExtractedAt = DateTimeOffset.UtcNow
                };

                await _database.InsertAttachmentAsync(imageEntity, cancellationToken);
            }
            else
            {
                // Extract regular attachment to attachments/{email_filename}_attachments/ folder
                var originalFilename = bodyPart.FileName ?? $"attachment_{attachmentCount}";
                var filename = SanitizeFilename(originalFilename);

                // Check if this is an executable file (only if skipExecutables is enabled)
                var blockedExtension = skipExecutables ? SecurityHelper.GetBlockedExtension(filename) : null;
                if (blockedExtension != null)
                {
                    // Create attachments directory if needed
                    if (!attachmentDirCreated)
                    {
                        Directory.CreateDirectory(Path.Combine(_archiveRoot, attachmentDir));
                        attachmentDirCreated = true;
                    }

                    // Create a .skipped placeholder file instead
                    var skippedFilename = filename + ".skipped";
                    var skippedPath = Path.Combine(attachmentDir, skippedFilename);
                    var fullSkippedPath = Path.Combine(_archiveRoot, skippedPath);

                    var placeholder = SecurityHelper.GenerateSkippedPlaceholder(
                        originalFilename,
                        message.LocalPath,
                        $"Executable file type ({blockedExtension})");

                    await File.WriteAllTextAsync(fullSkippedPath, placeholder, cancellationToken);

                    _logger.Info("Skipped executable attachment: {0} - created placeholder {1}", originalFilename, skippedFilename);

                    // Record skipped attachment in database
                    var skippedAttachment = new Attachment
                    {
                        MessageId = message.GraphId,
                        Filename = originalFilename,
                        FilePath = skippedPath,
                        SizeBytes = 0,
                        ContentType = bodyPart.ContentType?.MimeType ?? "application/octet-stream",
                        IsInline = false,
                        ContentId = null,
                        Skipped = true,
                        SkipReason = $"executable:{blockedExtension}",
                        ExtractedAt = DateTimeOffset.UtcNow
                    };

                    await _database.InsertAttachmentAsync(skippedAttachment, cancellationToken);
                    attachmentCount++;
                    hasContent = true;
                    continue;
                }

                // Create attachments directory if needed
                if (!attachmentDirCreated)
                {
                    Directory.CreateDirectory(Path.Combine(_archiveRoot, attachmentDir));
                    attachmentDirCreated = true;
                }

                var outputPath = Path.Combine(attachmentDir, filename);
                var fullPath = Path.Combine(_archiveRoot, outputPath);

                // Handle filename collisions
                var counter = 1;
                while (File.Exists(fullPath))
                {
                    var ext = Path.GetExtension(filename);
                    var baseName = Path.GetFileNameWithoutExtension(filename);
                    filename = $"{baseName}_{counter}{ext}";
                    outputPath = Path.Combine(attachmentDir, filename);
                    fullPath = Path.Combine(_archiveRoot, outputPath);
                    counter++;
                }

                // Use explicit block scope to ensure stream is closed before reading file size
                {
                    using var stream = File.Create(fullPath);
                    await bodyPart.Content.DecodeToAsync(stream, cancellationToken);
                }
                attachmentCount++;
                hasContent = true;

                var fileSize = new FileInfo(fullPath).Length;

                // Record attachment in database
                var attachmentEntity = new Attachment
                {
                    MessageId = message.GraphId,
                    Filename = originalFilename,
                    FilePath = outputPath,
                    SizeBytes = fileSize,
                    ContentType = bodyPart.ContentType?.MimeType ?? "application/octet-stream",
                    IsInline = false,
                    ContentId = null,
                    Skipped = false,
                    SkipReason = null,
                    ExtractedAt = DateTimeOffset.UtcNow
                };

                var attachmentId = await _database.InsertAttachmentAsync(attachmentEntity, cancellationToken);

                // Check if this is a ZIP file and extract it
                if (ZipExtractor.IsZipFile(filename))
                {
                    await ExtractZipAttachmentAsync(
                        message,
                        attachmentId,
                        fullPath,
                        Path.Combine(_archiveRoot, attachmentDir),
                        Path.Combine(attachmentDir, filename + "_extracted"),
                        cancellationToken);
                }
            }
        }

        return hasContent ? attachmentDir : "no_attachments";
    }

    /// <summary>
    /// Gets a file extension from a MIME content type.
    /// </summary>
    private static string? GetExtensionFromContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return null;

        return contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/svg+xml" => ".svg",
            "image/tiff" => ".tiff",
            "image/x-icon" => ".ico",
            _ => null
        };
    }

    private async Task ExtractZipAttachmentAsync(
        Message message,
        long attachmentId,
        string zipFullPath,
        string attachmentDir,
        string extractionRelativePath,
        CancellationToken cancellationToken)
    {
        var extractionFullPath = Path.Combine(_archiveRoot, extractionRelativePath);
        var zipFilename = Path.GetFileName(zipFullPath);

        // Perform extraction
        var result = await _zipExtractor.ExtractAsync(zipFullPath, extractionFullPath, cancellationToken);

        // Record in database
        var zipExtraction = new ZipExtraction
        {
            AttachmentId = attachmentId,
            MessageId = message.GraphId,
            ZipFilename = zipFilename,
            ExtractionPath = result.Extracted ? extractionRelativePath : "",
            Extracted = result.Extracted,
            SkipReason = result.SkipReason,
            FileCount = result.Extracted ? result.FileCount : result.Analysis?.FileCount,
            TotalSizeBytes = result.Extracted ? result.TotalSizeBytes : result.Analysis?.TotalUncompressedSize,
            HasExecutables = result.Analysis?.HasExecutables ?? false,
            HasUnsafePaths = result.Analysis?.HasUnsafePaths ?? false,
            IsEncrypted = result.Analysis?.IsEncrypted ?? false,
            ExtractedAt = DateTimeOffset.UtcNow
        };

        var zipExtractionId = await _database.InsertZipExtractionAsync(zipExtraction, cancellationToken);

        // Record extracted files
        if (result.Extracted && result.ExtractedFiles.Count > 0)
        {
            var files = result.ExtractedFiles.Select(f => new ZipExtractedFile
            {
                ZipExtractionId = zipExtractionId,
                RelativePath = f.RelativePath,
                ExtractedPath = Path.Combine(extractionRelativePath, f.RelativePath),
                SizeBytes = f.SizeBytes
            }).ToList();

            await _database.InsertZipExtractedFilesAsync(files, cancellationToken);
        }
    }

    private static string BuildOutputDirectory(string outputType, string folderPath, DateTimeOffset receivedTime)
    {
        var basePath = Path.Combine(
            "transformed",
            folderPath,
            receivedTime.Year.ToString("D4", CultureInfo.InvariantCulture),
            receivedTime.Month.ToString("D2", CultureInfo.InvariantCulture));

        // For attachments and images, add the subfolder
        return outputType switch
        {
            "attachments" => Path.Combine(basePath, "attachments"),
            "images" => Path.Combine(basePath, "images"),
            _ => basePath
        };
    }

    private static string GenerateHtml(MimeMessage message, string outputPath, IReadOnlyList<Attachment>? attachments, string folderPath, HtmlTransformOptions? htmlOptions, string? immutableId)
    {
        var body = message.HtmlBody ?? message.TextBody ?? "";

        // If we only have text, wrap it in basic HTML
        if (string.IsNullOrEmpty(message.HtmlBody) && !string.IsNullOrEmpty(message.TextBody))
        {
            body = $"<pre>{System.Net.WebUtility.HtmlEncode(message.TextBody)}</pre>";
        }

        // Rewrite cid: references to point to extracted inline images
        if (attachments != null && !string.IsNullOrEmpty(body))
        {
            body = RewriteCidReferences(body, outputPath, attachments);
        }

        // Strip external images if configured
        if (htmlOptions?.StripExternalImages == true && !string.IsNullOrEmpty(body))
        {
            body = StripExternalImageReferences(body);
        }

        // Build optional CC line (respects HideCc config)
        var hideCc = htmlOptions?.HideCc ?? false;
        var ccLine = !hideCc && message.Cc != null && message.Cc.Count > 0
            ? $"            <div><strong>CC:</strong> {System.Net.WebUtility.HtmlEncode(message.Cc.ToString())}</div>\n"
            : "";

        // Build optional BCC line (respects HideBcc config, defaults to true)
        var hideBcc = htmlOptions?.HideBcc ?? true;
        var bccLine = !hideBcc && message.Bcc != null && message.Bcc.Count > 0
            ? $"            <div><strong>BCC:</strong> {System.Net.WebUtility.HtmlEncode(message.Bcc.ToString())}</div>\n"
            : "";

        // Build attachments section
        var attachmentsSection = GenerateAttachmentsHtml(outputPath, attachments);

        // Build Outlook link (if enabled and ImmutableId available)
        var outlookLink = "";
        if (htmlOptions?.IncludeOutlookLink == true && !string.IsNullOrEmpty(immutableId))
        {
            outlookLink = OutlookLinkHelper.GenerateHtmlLink(immutableId, htmlOptions.Mailbox);
        }

        // Build breadcrumb navigation
        var breadcrumb = BreadcrumbHelper.GenerateHtmlBreadcrumb(outputPath, message.Subject ?? "(no subject)");

        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{System.Net.WebUtility.HtmlEncode(message.Subject ?? "(no subject)")}</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; margin: 20px; max-width: 800px; }}
        .breadcrumb {{ padding: 10px 15px; margin-bottom: 15px; background: #f8f9fa; border-bottom: 1px solid #e0e0e0; font-size: 0.9em; border-radius: 5px; }}
        .breadcrumb a {{ color: #0078d4; text-decoration: none; }}
        .breadcrumb a:hover {{ text-decoration: underline; }}
        .breadcrumb .current {{ color: #666; }}
        .header {{ background: #f5f5f5; padding: 15px; margin-bottom: 20px; border-radius: 5px; }}
        .header h1 {{ margin: 0 0 10px 0; font-size: 1.2em; }}
        .header .meta {{ color: #666; font-size: 0.9em; }}
        .body {{ line-height: 1.6; }}
        .attachments {{ margin-top: 10px; }}
        .attachments ul {{ list-style-type: none; padding-left: 0; margin: 5px 0; }}
        .attachments li {{ padding: 2px 0; }}
        .attachments a {{ color: #0066cc; text-decoration: none; }}
        .attachments a:hover {{ text-decoration: underline; }}
        .skipped {{ color: #999; font-style: italic; }}
    </style>
</head>
<body>
    {breadcrumb}
    <div class=""header"">
        <h1>{System.Net.WebUtility.HtmlEncode(message.Subject ?? "(no subject)")}</h1>
        <div class=""meta"">
            <div><strong>From:</strong> {System.Net.WebUtility.HtmlEncode(message.From?.ToString() ?? "")}</div>
            <div><strong>To:</strong> {System.Net.WebUtility.HtmlEncode(message.To?.ToString() ?? "")}</div>
{ccLine}{bccLine}            <div><strong>Date:</strong> {message.Date:yyyy-MM-dd HH:mm:ss}</div>
{outlookLink}{attachmentsSection}        </div>
    </div>
    <div class=""body"">
        {body}
    </div>
</body>
</html>";
    }

    private static string GenerateMarkdown(MimeMessage message, string outputPath, IReadOnlyList<Attachment>? attachments, string folderPath, HtmlTransformOptions? htmlOptions, string? immutableId)
    {
        var textBody = message.TextBody ?? "";

        // If we only have HTML, strip tags for a basic conversion
        if (string.IsNullOrEmpty(message.TextBody) && !string.IsNullOrEmpty(message.HtmlBody))
        {
            textBody = StripHtml(message.HtmlBody);
        }

        // Build optional CC fields (respects HideCc config)
        var hideCc = htmlOptions?.HideCc ?? false;
        var ccFrontMatter = !hideCc && message.Cc != null && message.Cc.Count > 0
            ? $"cc: \"{EscapeYamlString(message.Cc.ToString())}\"\n"
            : "";
        var ccLine = !hideCc && message.Cc != null && message.Cc.Count > 0
            ? $"**CC:** {message.Cc}\n"
            : "";

        // Build optional BCC fields (respects HideBcc config, defaults to true)
        var hideBcc = htmlOptions?.HideBcc ?? true;
        var bccFrontMatter = !hideBcc && message.Bcc != null && message.Bcc.Count > 0
            ? $"bcc: \"{EscapeYamlString(message.Bcc.ToString())}\"\n"
            : "";
        var bccLine = !hideBcc && message.Bcc != null && message.Bcc.Count > 0
            ? $"**BCC:** {message.Bcc}\n"
            : "";

        // Build attachments section
        var attachmentsSection = GenerateAttachmentsMarkdown(outputPath, attachments);

        // Build Outlook link (if enabled and ImmutableId available)
        var outlookFrontMatter = "";
        var outlookDisplayLine = "";
        if (htmlOptions?.IncludeOutlookLink == true && !string.IsNullOrEmpty(immutableId))
        {
            outlookFrontMatter = OutlookLinkHelper.GenerateMarkdownFrontMatter(immutableId, htmlOptions.Mailbox);
            outlookDisplayLine = OutlookLinkHelper.GenerateMarkdownDisplayLine(immutableId, htmlOptions.Mailbox);
        }

        // Build breadcrumb navigation
        var breadcrumb = BreadcrumbHelper.GenerateMarkdownBreadcrumb(outputPath, message.Subject ?? "(no subject)");

        return $@"---
subject: ""{EscapeYamlString(message.Subject ?? "")}""
from: ""{EscapeYamlString(message.From?.ToString() ?? "")}""
to: ""{EscapeYamlString(message.To?.ToString() ?? "")}""
{ccFrontMatter}{bccFrontMatter}date: {message.Date:yyyy-MM-ddTHH:mm:sszzz}
{outlookFrontMatter}---

{breadcrumb}

# {message.Subject ?? "(no subject)"}

**From:** {message.From}
**To:** {message.To}
{ccLine}{bccLine}**Date:** {message.Date:yyyy-MM-dd HH:mm:ss}
{outlookDisplayLine}{attachmentsSection}
---

{textBody}
";
    }

    private static string StripHtml(string html)
    {
        // Simple HTML stripping - for proper conversion, would use a library
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "");
        text = System.Net.WebUtility.HtmlDecode(text);
        return text.Trim();
    }

    /// <summary>
    /// Rewrites cid: references in HTML to point to extracted inline images.
    /// </summary>
    /// <param name="html">The HTML body content</param>
    /// <param name="outputPath">The output path of the HTML file (for calculating relative paths)</param>
    /// <param name="attachments">The list of attachments including inline images with ContentId</param>
    /// <returns>HTML with cid: references replaced with relative paths to images</returns>
    private static string RewriteCidReferences(string html, string outputPath, IReadOnlyList<Attachment> attachments)
    {
        // Build a mapping from ContentId to file path for inline images
        var cidToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attachment in attachments)
        {
            if (attachment.IsInline && !string.IsNullOrEmpty(attachment.ContentId))
            {
                // Calculate relative path from output file to the image
                var relativePath = CalculateRelativePathToAttachment(outputPath, attachment.FilePath);
                cidToPath[attachment.ContentId] = relativePath;
            }
        }

        if (cidToPath.Count == 0)
            return html;

        // Replace cid: references with relative paths
        // Matches: src="cid:xxx" or src='cid:xxx'
        return System.Text.RegularExpressions.Regex.Replace(
            html,
            @"(src\s*=\s*[""'])cid:([^""']+)([""'])",
            match =>
            {
                var prefix = match.Groups[1].Value;
                var cidValue = match.Groups[2].Value;
                var suffix = match.Groups[3].Value;

                // Try to find the ContentId (may or may not have angle brackets)
                var lookupCid = cidValue.Trim();
                if (cidToPath.TryGetValue(lookupCid, out var relativePath))
                {
                    return $"{prefix}{relativePath}{suffix}";
                }

                // Also try without angle brackets if the cid has them
                if (lookupCid.StartsWith('<') && lookupCid.EndsWith('>'))
                {
                    lookupCid = lookupCid[1..^1];
                    if (cidToPath.TryGetValue(lookupCid, out relativePath))
                    {
                        return $"{prefix}{relativePath}{suffix}";
                    }
                }

                // If not found, leave the original reference
                return match.Value;
            },
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Removes external image references (http/https) from HTML content.
    /// Preserves inline images (data: URIs) and relative paths.
    /// </summary>
    private static string StripExternalImageReferences(string html)
    {
        // Remove <img> tags with external src (http:// or https://)
        // Preserve data: URIs and relative paths
        return System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<img\s[^>]*src\s*=\s*[""']https?://[^""']*[""'][^>]*/??>",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
    }

    private static string EscapeYamlString(string value)
    {
        return value.Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");
    }

    private static string SanitizeFilename(string filename)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
        {
            filename = filename.Replace(c, '_');
        }
        return filename;
    }

    /// <summary>
    /// Calculates the relative path from an output file to an attachment.
    /// </summary>
    /// <param name="outputFilePath">Relative path to output file from archive root (e.g., "html/Inbox/2024/01/file.html")</param>
    /// <param name="attachmentFilePath">Relative path to attachment from archive root</param>
    /// <returns>Relative path with forward slashes for HTML/Markdown compatibility</returns>
    private static string CalculateRelativePathToAttachment(string outputFilePath, string attachmentFilePath)
    {
        // Get directory containing the output file
        var outputDir = Path.GetDirectoryName(outputFilePath);
        if (string.IsNullOrEmpty(outputDir))
        {
            return attachmentFilePath.Replace(Path.DirectorySeparatorChar, '/');
        }

        // Normalize separators for consistent splitting
        var normalizedOutputDir = outputDir.Replace(Path.DirectorySeparatorChar, '/');
        var normalizedAttachmentPath = attachmentFilePath.Replace(Path.DirectorySeparatorChar, '/');

        // Split both paths into components
        var outputParts = normalizedOutputDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var attachmentParts = normalizedAttachmentPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Find common prefix length
        var commonLength = 0;
        var minLength = Math.Min(outputParts.Length, attachmentParts.Length);
        for (var i = 0; i < minLength; i++)
        {
            if (outputParts[i].Equals(attachmentParts[i], StringComparison.OrdinalIgnoreCase))
                commonLength++;
            else
                break;
        }

        // Build relative path: go up from output dir, then down to attachment
        var upCount = outputParts.Length - commonLength;
        var upParts = Enumerable.Repeat("..", upCount);
        var downParts = attachmentParts.Skip(commonLength);

        return string.Join("/", upParts.Concat(downParts));
    }

    /// <summary>
    /// Formats a file size in bytes to a human-readable string.
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    /// <summary>
    /// Generates the HTML attachments section.
    /// </summary>
    private static string GenerateAttachmentsHtml(string outputPath, IReadOnlyList<Attachment>? attachments)
    {
        if (attachments == null || attachments.Count == 0)
            return "";

        // Filter to only non-inline attachments
        var regularAttachments = attachments.Where(a => !a.IsInline).ToList();
        if (regularAttachments.Count == 0)
            return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("            <div class=\"attachments\">");
        sb.AppendLine("                <strong>Attachments:</strong>");
        sb.AppendLine("                <ul>");

        foreach (var attachment in regularAttachments)
        {
            var displayName = System.Net.WebUtility.HtmlEncode(attachment.Filename);

            if (attachment.Skipped)
            {
                var reason = System.Net.WebUtility.HtmlEncode(attachment.SkipReason ?? "Skipped");
                sb.AppendLine(CultureInfo.InvariantCulture, $"                    <li><span class=\"skipped\" title=\"{reason}\">{displayName}</span> (skipped)</li>");
            }
            else
            {
                var relativePath = CalculateRelativePathToAttachment(outputPath, attachment.FilePath);
                var sizeFormatted = FormatFileSize(attachment.SizeBytes);
                sb.AppendLine(CultureInfo.InvariantCulture, $"                    <li><a href=\"{relativePath}\">{displayName}</a> ({sizeFormatted})</li>");
            }
        }

        sb.AppendLine("                </ul>");
        sb.AppendLine("            </div>");

        return sb.ToString();
    }

    /// <summary>
    /// Generates the Markdown attachments section.
    /// </summary>
    private static string GenerateAttachmentsMarkdown(string outputPath, IReadOnlyList<Attachment>? attachments)
    {
        if (attachments == null || attachments.Count == 0)
            return "";

        // Filter to only non-inline attachments
        var regularAttachments = attachments.Where(a => !a.IsInline).ToList();
        if (regularAttachments.Count == 0)
            return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("**Attachments:**");

        foreach (var attachment in regularAttachments)
        {
            if (attachment.Skipped)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {attachment.Filename} (skipped: {attachment.SkipReason ?? "unknown"})");
            }
            else
            {
                var relativePath = CalculateRelativePathToAttachment(outputPath, attachment.FilePath);
                var sizeFormatted = FormatFileSize(attachment.SizeBytes);
                sb.AppendLine(CultureInfo.InvariantCulture, $"- [{attachment.Filename}]({relativePath}) ({sizeFormatted})");
            }
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public async Task<bool> TransformSingleMessageAsync(
        Message message,
        InlineTransformOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!options.HasAnyTransformation)
            return true;

        var success = true;

        try
        {
            // Extract attachments FIRST so that attachment links and sizes
            // are available when generating HTML/Markdown output
            if (options.ExtractAttachments)
            {
                var result = await TransformMessageAsync(message, "attachments", options.HtmlOptions, options.AttachmentOptions, cancellationToken);
                if (result == TransformMessageResult.Error)
                {
                    success = false;
                    _logger.Warning("Attachment extraction failed for message {0}", message.GraphId);
                }
            }

            if (options.GenerateHtml)
            {
                var result = await TransformMessageAsync(message, "html", options.HtmlOptions, options.AttachmentOptions, cancellationToken);
                if (result == TransformMessageResult.Error)
                {
                    success = false;
                    _logger.Warning("HTML transformation failed for message {0}", message.GraphId);
                }
            }

            if (options.GenerateMarkdown)
            {
                var result = await TransformMessageAsync(message, "markdown", options.HtmlOptions, options.AttachmentOptions, cancellationToken);
                if (result == TransformMessageResult.Error)
                {
                    success = false;
                    _logger.Warning("Markdown transformation failed for message {0}", message.GraphId);
                }
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during inline transformation of message {0}: {1}",
                message.GraphId, ex.Message);
            return false;
        }
    }

    private enum TransformMessageResult
    {
        Transformed,
        Skipped,
        Error
    }
}
