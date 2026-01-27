using CliFx.Attributes;
using CliFx.Infrastructure;
using M365MailMirror.Core.Configuration;
using M365MailMirror.Core.Exceptions;
using M365MailMirror.Core.Logging;
using M365MailMirror.Core.Transform;
using M365MailMirror.Infrastructure.Database;
using M365MailMirror.Infrastructure.Storage;
using M365MailMirror.Infrastructure.Transform;

namespace M365MailMirror.Cli.Commands;

[Command("transform", Description = "Transform EML files to HTML, Markdown, and extract attachments")]
public class TransformCommand : BaseCommand
{
    [CommandOption("config", 'c', Description = "Path to configuration file")]
    public string? ConfigPath { get; init; }

    [CommandOption("archive", 'a', Description = "Path to the mail archive")]
    public string? ArchivePath { get; init; }

    [CommandOption("only", Description = "Only run specific transformation (html, markdown, attachments)")]
    public string? Only { get; init; }

    [CommandOption("force", Description = "Force regeneration even if already transformed")]
    public bool Force { get; init; }

    [CommandOption("clean", Description = "Delete all transformed content and regenerate from EML files")]
    public bool Clean { get; init; }

    [CommandOption("yes", 'y', Description = "Skip confirmation prompt when using --clean")]
    public bool Yes { get; init; }

    [CommandOption("parallel", 'p', Description = "Number of parallel transformations")]
    public int Parallel { get; init; } = 5;

    [CommandOption("max", 'm', Description = "Maximum messages to transform per type (0 = unlimited)")]
    public int Max { get; init; }

    [CommandOption("path", Description = "Transform EML files at a specific path (file or folder). Absolute paths are converted to relative. Folders are processed recursively.")]
    public string? FilterPath { get; init; }

    [CommandOption("verbose", 'v', Description = "Show detailed debug logging")]
    public bool Verbose { get; init; }

    [CommandOption("html", Description = "Enable HTML transformation (default: from config)")]
    public bool? Html { get; init; }

    [CommandOption("markdown", Description = "Enable Markdown transformation (default: from config)")]
    public bool? Markdown { get; init; }

    [CommandOption("attachments", Description = "Enable attachment extraction (default: from config)")]
    public bool? Attachments { get; init; }

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        ConfigureLogging(console, Verbose);
        var logger = LoggerFactory.CreateLogger<TransformCommand>();
        var cancellationToken = console.RegisterCancellationHandler();

        // Load configuration
        var config = ConfigurationLoader.Load(ConfigPath);
        var archiveRoot = ArchivePath ?? config.OutputPath;

        // Apply transform configuration (CLI flags override config values)
        var enableHtml = Html ?? config.Transform.GenerateHtml;
        var enableMarkdown = Markdown ?? config.Transform.GenerateMarkdown;
        var enableAttachments = Attachments ?? config.Transform.ExtractAttachments;

        // Diagnostic output to debug transformation type selection
        await console.Output.WriteLineAsync($"Transforms enabled: HTML={enableHtml}, Markdown={enableMarkdown}, Attachments={enableAttachments}");

        // Verify archive directory exists
        if (!Directory.Exists(archiveRoot))
        {
            throw new M365MailMirrorException($"Archive directory does not exist: {archiveRoot}", CliExitCodes.FileSystemError);
        }

        // Check if database exists in status subdirectory
        var databasePath = Path.Combine(archiveRoot, StateDatabase.DatabaseDirectory, StateDatabase.DefaultDatabaseFilename);
        if (!System.IO.File.Exists(databasePath))
        {
            throw new M365MailMirrorException($"No archive database found at: {databasePath}. Run 'sync' first to create the archive.", CliExitCodes.FileSystemError);
        }

        // Validate --path option and normalize path
        string? normalizedFilterPath = null;
        bool filterPathIsDirectory = false;
        if (!string.IsNullOrEmpty(FilterPath))
        {
            string fullPath;
            string relativePath;

            // Check if the path is absolute
            if (Path.IsPathRooted(FilterPath))
            {
                fullPath = Path.GetFullPath(FilterPath);
                var normalizedArchiveRoot = Path.GetFullPath(archiveRoot);

                // Ensure archive root ends with directory separator for proper prefix matching
                if (!normalizedArchiveRoot.EndsWith(Path.DirectorySeparatorChar))
                {
                    normalizedArchiveRoot += Path.DirectorySeparatorChar;
                }

                if (!fullPath.StartsWith(normalizedArchiveRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new M365MailMirrorException(
                        $"Path '{FilterPath}' is not within the archive directory '{archiveRoot}'. " +
                        "The path must be located within the archive root.",
                        CliExitCodes.FileSystemError);
                }

                relativePath = fullPath[normalizedArchiveRoot.Length..];
            }
            else
            {
                relativePath = FilterPath;
                fullPath = Path.Combine(archiveRoot, FilterPath);
            }

            // Determine if it's a file or directory
            if (System.IO.File.Exists(fullPath))
            {
                filterPathIsDirectory = false;
                normalizedFilterPath = relativePath;
            }
            else if (Directory.Exists(fullPath))
            {
                filterPathIsDirectory = true;
                // For directories, keep the full relative path (e.g., eml/Inbox/2024/01)
                // We query by local_path prefix which includes the year/month structure
                normalizedFilterPath = relativePath.Replace(Path.DirectorySeparatorChar, '/').TrimEnd('/');
            }
            else
            {
                throw new M365MailMirrorException($"Path not found: {fullPath}", CliExitCodes.FileSystemError);
            }
        }

        await console.Output.WriteLineAsync($"Archive: {archiveRoot}");
        await console.Output.WriteLineAsync($"Parallel: {Parallel}");

        if (Max > 0)
        {
            await console.Output.WriteLineAsync($"Max messages per type: {Max}");
        }

        if (!string.IsNullOrEmpty(Only))
        {
            await console.Output.WriteLineAsync($"Only: {Only}");
        }

        if (!string.IsNullOrEmpty(normalizedFilterPath))
        {
            if (filterPathIsDirectory)
            {
                await console.Output.WriteLineAsync($"Filter folder: {normalizedFilterPath}");
            }
            else
            {
                await console.Output.WriteLineAsync($"Single file: {normalizedFilterPath}");
            }
        }

        if (Force && !Clean)
        {
            await WriteWarningAsync(console, "Force mode: regenerating all transformations");
        }

        await console.Output.WriteLineAsync();

        // Create services
        await using var database = new StateDatabase(databasePath, logger);
        await database.InitializeAsync(cancellationToken);

        // Handle --clean option
        if (Clean)
        {
            var transformedPath = Path.Combine(archiveRoot, "transformed");

            // Prompt for confirmation unless --yes is passed
            if (!Yes)
            {
                await WriteWarningAsync(console, "This will delete all transformed content:");
                await console.Output.WriteLineAsync($"  Directory: {transformedPath}");
                await console.Output.WriteLineAsync("  Database records: transformations, attachments, ZIP extractions");
                await console.Output.WriteLineAsync();
                await console.Output.WriteAsync("Are you sure you want to continue? [y/N] ");

                var response = await console.Input.ReadLineAsync();
                if (string.IsNullOrEmpty(response) ||
                    !response.Equals("y", StringComparison.OrdinalIgnoreCase))
                {
                    await console.Output.WriteLineAsync("Clean operation cancelled.");
                    return;
                }
            }

            // Delete transformed directory
            if (Directory.Exists(transformedPath))
            {
                await console.Output.WriteLineAsync();
                await console.Output.WriteLineAsync("Deleting transformed directory...");
                try
                {
                    Directory.Delete(transformedPath, recursive: true);
                    await console.Output.WriteLineAsync($"  Deleted: {transformedPath}");
                }
                catch (Exception ex)
                {
                    await WriteWarningAsync(console, $"Failed to delete transformed directory: {ex.Message}");
                    // Continue anyway - transformation will overwrite files
                }
            }
            else
            {
                await console.Output.WriteLineAsync("Transformed directory does not exist, skipping deletion.");
            }

            // Clear database records
            await console.Output.WriteLineAsync("Clearing transformation records from database...");
            var clearedCount = await database.ClearAllTransformationDataAsync(cancellationToken);
            await console.Output.WriteLineAsync($"  Cleared {clearedCount} transformation records");
            await console.Output.WriteLineAsync();
        }

        var emlStorage = new EmlStorageService(archiveRoot, logger);
        var transformService = new TransformationService(database, emlStorage, archiveRoot, config.ZipExtraction, logger);

        // Build transform options
        var options = new TransformOptions
        {
            Only = Only,
            Force = Force || Clean,
            MaxParallel = Parallel,
            MaxMessages = Max,
            FilterPath = normalizedFilterPath,
            FilterPathIsDirectory = filterPathIsDirectory,
            EnableHtml = enableHtml,
            EnableMarkdown = enableMarkdown,
            EnableAttachments = enableAttachments,
            HtmlOptions = new HtmlTransformOptions
            {
                InlineStyles = config.Transform.Html.InlineStyles,
                StripExternalImages = config.Transform.Html.StripExternalImages,
                HideCc = config.Transform.Html.HideCc,
                HideBcc = config.Transform.Html.HideBcc,
                IncludeOutlookLink = config.Transform.Html.IncludeOutlookLink,
                Mailbox = config.Mailbox
            },
            AttachmentOptions = new AttachmentExtractOptions
            {
                SkipExecutables = config.Attachments.SkipExecutables
            }
        };

        // Execute transformation with progress reporting
        await console.Output.WriteLineAsync("Starting transformation...");
        await console.Output.WriteLineAsync();

        var lastType = "";
        var result = await transformService.TransformAsync(options, progress =>
        {
            if (progress.TransformationType != lastType)
            {
                lastType = progress.TransformationType ?? "";
                if (!string.IsNullOrEmpty(lastType))
                {
                    console.Output.WriteLine($"[{progress.Phase}] {lastType}");
                }
            }
        }, cancellationToken);

        // Generate navigation indexes after transformation (skip when using --path filter)
        if ((enableHtml || enableMarkdown) && result.Success && string.IsNullOrEmpty(normalizedFilterPath))
        {
            await console.Output.WriteLineAsync();
            await console.Output.WriteLineAsync("Generating navigation indexes...");

            var indexService = new IndexGenerationService(database, archiveRoot, logger);
            var indexResult = await indexService.GenerateIndexesAsync(
                new IndexGenerationOptions
                {
                    GenerateHtmlIndexes = enableHtml,
                    GenerateMarkdownIndexes = enableMarkdown
                },
                cancellationToken);

            if (indexResult.Success)
            {
                await console.Output.WriteLineAsync($"  Generated {indexResult.HtmlIndexesGenerated} HTML indexes, {indexResult.MarkdownIndexesGenerated} Markdown indexes");
            }
            else
            {
                await WriteWarningAsync(console, $"Index generation failed: {indexResult.ErrorMessage}");
            }
        }

        // Display results
        await console.Output.WriteLineAsync();

        if (result.Success)
        {
            await WriteSuccessAsync(console, "Transformation completed successfully!");
        }
        else if (result.WasCancelled)
        {
            await WriteWarningAsync(console, "Transformation cancelled by user.");
        }
        else
        {
            await WriteErrorAsync(console, $"Transformation failed: {result.ErrorMessage}");
        }

        await console.Output.WriteLineAsync();
        await console.Output.WriteLineAsync($"Messages transformed: {result.MessagesTransformed}");
        await console.Output.WriteLineAsync($"Messages skipped:     {result.MessagesSkipped}");

        if (result.Errors > 0)
        {
            await WriteWarningAsync(console, $"Errors:               {result.Errors}");
        }

        await console.Output.WriteLineAsync($"Elapsed time:         {result.Elapsed:hh\\:mm\\:ss}");
    }
}
