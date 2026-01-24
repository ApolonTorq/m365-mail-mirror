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

    [CommandOption("parallel", 'p', Description = "Number of parallel transformations")]
    public int Parallel { get; init; } = 5;

    [CommandOption("html", Description = "Enable HTML transformation (default: from config)")]
    public bool? Html { get; init; }

    [CommandOption("markdown", Description = "Enable Markdown transformation (default: from config)")]
    public bool? Markdown { get; init; }

    [CommandOption("attachments", Description = "Enable attachment extraction (default: from config)")]
    public bool? Attachments { get; init; }

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        ConfigureLogging(console);
        var logger = LoggerFactory.CreateLogger<TransformCommand>();
        var cancellationToken = console.RegisterCancellationHandler();

        // Load configuration
        var config = ConfigurationLoader.Load(ConfigPath);
        var archiveRoot = ArchivePath ?? config.OutputPath;

        // Apply transform configuration (CLI flags override config values)
        var enableHtml = Html ?? config.Transform.GenerateHtml;
        var enableMarkdown = Markdown ?? config.Transform.GenerateMarkdown;
        var enableAttachments = Attachments ?? config.Transform.ExtractAttachments;

        // Verify archive directory exists
        if (!Directory.Exists(archiveRoot))
        {
            throw new M365MailMirrorException($"Archive directory does not exist: {archiveRoot}", CliExitCodes.FileSystemError);
        }

        // Check if database exists in status subdirectory
        var databasePath = Path.Combine(archiveRoot, StateDatabase.DatabaseDirectory, StateDatabase.DefaultDatabaseFilename);
        if (!File.Exists(databasePath))
        {
            throw new M365MailMirrorException($"No archive database found at: {databasePath}. Run 'sync' first to create the archive.", CliExitCodes.FileSystemError);
        }

        await console.Output.WriteLineAsync($"Archive: {archiveRoot}");
        await console.Output.WriteLineAsync($"Parallel: {Parallel}");

        if (!string.IsNullOrEmpty(Only))
        {
            await console.Output.WriteLineAsync($"Only: {Only}");
        }

        if (Force)
        {
            await WriteWarningAsync(console, "Force mode: regenerating all transformations");
        }

        await console.Output.WriteLineAsync();

        // Create services
        await using var database = new StateDatabase(databasePath, logger);
        await database.InitializeAsync(cancellationToken);

        var emlStorage = new EmlStorageService(archiveRoot, logger);
        var transformService = new TransformationService(database, emlStorage, archiveRoot, config.ZipExtraction, logger);

        // Build transform options
        var options = new TransformOptions
        {
            Only = Only,
            Force = Force,
            MaxParallel = Parallel,
            EnableHtml = enableHtml,
            EnableMarkdown = enableMarkdown,
            EnableAttachments = enableAttachments,
            HtmlOptions = new HtmlTransformOptions
            {
                InlineStyles = config.Transform.Html.InlineStyles,
                StripExternalImages = config.Transform.Html.StripExternalImages,
                HideCc = config.Transform.Html.HideCc,
                HideBcc = config.Transform.Html.HideBcc
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

        // Generate navigation indexes after transformation
        if ((enableHtml || enableMarkdown) && result.Success)
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
