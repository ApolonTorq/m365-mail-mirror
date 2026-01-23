using CliFx.Attributes;
using CliFx.Infrastructure;
using M365MailMirror.Core.Configuration;
using M365MailMirror.Core.Exceptions;
using M365MailMirror.Core.Logging;
using M365MailMirror.Core.Sync;
using M365MailMirror.Core.Transform;
using M365MailMirror.Infrastructure.Authentication;
using M365MailMirror.Infrastructure.Database;
using M365MailMirror.Infrastructure.Graph;
using M365MailMirror.Infrastructure.Storage;
using M365MailMirror.Infrastructure.Sync;
using M365MailMirror.Infrastructure.Transform;
using Microsoft.Graph;

namespace M365MailMirror.Cli.Commands;

[Command("sync", Description = "Download and sync emails from Microsoft 365 mailbox")]
public class SyncCommand : BaseCommand
{
    [CommandOption("config", 'c', Description = "Path to configuration file")]
    public string? ConfigPath { get; init; }

    [CommandOption("output", 'o', Description = "Output directory for the mail archive")]
    public string? OutputPath { get; init; }

    [CommandOption("verbose", 'v', Description = "Show detailed debug logging")]
    public bool Verbose { get; init; }

    [CommandOption("checkpoint-interval", 'b', Description = "Number of messages between checkpoints (default: 10)")]
    public int CheckpointInterval { get; init; } = 10;

    [CommandOption("parallel", 'p', Description = "Number of parallel downloads")]
    public int Parallel { get; init; } = 5;

    [CommandOption("dry-run", Description = "Show what would be downloaded without actually downloading")]
    public bool DryRun { get; init; }

    [CommandOption("folder", 'f', Description = "Specific folder to sync (default: all folders)")]
    public string? Folder { get; init; }

    [CommandOption("exclude", 'x', Description = "Folders to exclude from sync")]
    public IReadOnlyList<string> ExcludeFolders { get; init; } = [];

    [CommandOption("mailbox", 'm', Description = "Email address of mailbox to sync (default: authenticated user)")]
    public string? Mailbox { get; init; }

    [CommandOption("html", Description = "Generate HTML transformations for synced messages")]
    public bool GenerateHtml { get; init; }

    [CommandOption("markdown", Description = "Generate Markdown transformations for synced messages")]
    public bool GenerateMarkdown { get; init; }

    [CommandOption("attachments", Description = "Extract attachments from synced messages")]
    public bool ExtractAttachments { get; init; }

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        ConfigureLogging(console, Verbose);
        var logger = LoggerFactory.CreateLogger<SyncCommand>();
        var cancellationToken = console.RegisterCancellationHandler();

        // Load configuration
        var config = ConfigurationLoader.Load(ConfigPath);

        // Apply command-line overrides
        var outputPath = OutputPath ?? config.OutputPath;
        var checkpointInterval = CheckpointInterval > 0 ? CheckpointInterval : config.Sync.CheckpointInterval;
        var parallel = Parallel > 0 ? Parallel : config.Sync.Parallel;
        var excludeFolders = ExcludeFolders.Count > 0 ? ExcludeFolders : config.Sync.ExcludeFolders;
        var mailbox = Mailbox ?? config.Mailbox;

        if (string.IsNullOrEmpty(config.ClientId))
        {
            throw new ConfigurationException("Client ID is required. Run 'auth login' first or provide a configuration file.");
        }

        // Verify output directory exists
        var archiveRoot = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(archiveRoot);

        // Show sync configuration
        if (DryRun)
        {
            await WriteWarningAsync(console, "DRY RUN MODE - No files will be written");
            await console.Output.WriteLineAsync();
        }

        await console.Output.WriteLineAsync($"Archive directory: {archiveRoot}");
        await console.Output.WriteLineAsync($"Checkpoint interval: {checkpointInterval}");
        await console.Output.WriteLineAsync($"Parallel downloads: {parallel}");

        if (excludeFolders.Count > 0)
        {
            await console.Output.WriteLineAsync($"Excluded folders: {string.Join(", ", excludeFolders)}");
        }

        await console.Output.WriteLineAsync();

        // Set up authentication
        await console.Output.WriteLineAsync("Authenticating...");

        var tokenCache = new FileTokenCacheStorage();
        var authService = new MsalAuthenticationService(config.ClientId, config.TenantId, tokenCache, logger);

        // Acquire token silently - this validates auth and provides account info in one call
        // (Previously called GetStatusAsync first, but that internally calls AcquireTokenSilent,
        // causing redundant MSAL calls that can trigger AAD throttling)
        var tokenResult = await authService.AcquireTokenSilentAsync(cancellationToken);
        if (!tokenResult.IsSuccess)
        {
            throw new M365MailMirrorException(
                tokenResult.ErrorMessage ?? "Not authenticated. Run 'auth login' first.",
                CliExitCodes.AuthenticationError);
        }

        await console.Output.WriteLineAsync($"Authenticated as: {tokenResult.Account}");
        await console.Output.WriteLineAsync();

        // Create Graph client using a token credential wrapper
        var tokenCredential = new DelegateTokenCredential(authService);
        var graphClient = new GraphServiceClient(tokenCredential);
        var graphMailClient = new GraphMailClient(graphClient, logger);

        // Create database in status subdirectory
        var statusDir = Path.Combine(archiveRoot, StateDatabase.DatabaseDirectory);
        Directory.CreateDirectory(statusDir);
        var databasePath = Path.Combine(statusDir, StateDatabase.DefaultDatabaseFilename);
        await using var database = new StateDatabase(databasePath, logger);
        await database.InitializeAsync(cancellationToken);

        // Create EML storage
        var emlStorage = new EmlStorageService(archiveRoot, logger);

        // Create transformation service only if any transformation flags are set
        ITransformationService? transformationService = null;
        if (GenerateHtml || GenerateMarkdown || ExtractAttachments)
        {
            transformationService = new TransformationService(
                database,
                emlStorage,
                archiveRoot,
                config.ZipExtraction,
                logger);

            // Log which transformations are enabled
            var enabledTransforms = new List<string>();
            if (GenerateHtml) enabledTransforms.Add("HTML");
            if (GenerateMarkdown) enabledTransforms.Add("Markdown");
            if (ExtractAttachments) enabledTransforms.Add("Attachments");
            await console.Output.WriteLineAsync($"Inline transformations: {string.Join(", ", enabledTransforms)}");
            await console.Output.WriteLineAsync();
        }

        // Create sync engine with optional transformation service
        var syncEngine = new SyncEngine(graphMailClient, database, emlStorage, logger, transformationService);

        // Build sync options
        var syncOptions = new SyncOptions
        {
            CheckpointInterval = checkpointInterval,
            MaxParallelDownloads = parallel,
            ExcludeFolders = excludeFolders.ToList(),
            DryRun = DryRun,
            Mailbox = mailbox,
            GenerateHtml = GenerateHtml,
            GenerateMarkdown = GenerateMarkdown,
            ExtractAttachments = ExtractAttachments
        };

        // Execute sync with progress reporting
        await console.Output.WriteLineAsync("Starting sync...");
        await console.Output.WriteLineAsync();

        var lastPhase = "";
        var lastFolder = "";
        var lastReportedSynced = 0;
        var lastReportedPage = 0;

        var result = await syncEngine.SyncAsync(syncOptions, progress =>
        {
            // Always show folder changes with summary info
            if (progress.Phase != lastPhase || progress.CurrentFolder != lastFolder)
            {
                // Show completion summary for previous folder if we had progress
                if (!string.IsNullOrEmpty(lastFolder) && lastPhase == "Downloading messages" && lastReportedSynced > 0)
                {
                    console.Output.WriteLine($"  Completed: {lastReportedSynced} messages synced");
                }

                lastPhase = progress.Phase;
                lastFolder = progress.CurrentFolder ?? "";
                lastReportedSynced = 0;
                lastReportedPage = 0;

                // Build informative progress line
                var folderProgress = progress.TotalFolders > 0
                    ? $" ({progress.ProcessedFolders + 1}/{progress.TotalFolders})"
                    : "";

                var folderInfo = !string.IsNullOrEmpty(progress.CurrentFolder)
                    ? $": {progress.CurrentFolder}{folderProgress}"
                    : "";

                var messageCount = progress.TotalMessagesInFolder > 0
                    ? $" [{progress.TotalMessagesInFolder} messages]"
                    : "";

                console.Output.WriteLine($"[{progress.Phase}]{folderInfo}{messageCount}");
            }

            // Show periodic progress during downloading (every 10 messages or new page)
            if (progress.Phase == "Downloading messages")
            {
                var shouldReport = progress.TotalMessagesSynced >= lastReportedSynced + 10 ||
                                   (progress.CurrentPage > lastReportedPage && progress.CurrentPage > 1);

                if (shouldReport)
                {
                    lastReportedSynced = progress.TotalMessagesSynced;
                    lastReportedPage = progress.CurrentPage;

                    var pageInfo = progress.CurrentPage > 0 ? $"page {progress.CurrentPage}, " : "";
                    console.Output.WriteLine($"  Progress: {pageInfo}{progress.TotalMessagesSynced} synced, {progress.ProcessedMessagesInFolder}/{progress.TotalMessagesInFolder} in folder");
                }
            }
        }, cancellationToken);

        // Generate navigation indexes if any transformations were enabled
        if ((GenerateHtml || GenerateMarkdown) && result.Success && result.MessagesSynced > 0)
        {
            await console.Output.WriteLineAsync();
            await console.Output.WriteLineAsync("Generating navigation indexes...");

            var indexService = new IndexGenerationService(database, archiveRoot, logger);
            var indexResult = await indexService.GenerateIndexesAsync(
                new IndexGenerationOptions
                {
                    GenerateHtmlIndexes = GenerateHtml,
                    GenerateMarkdownIndexes = GenerateMarkdown
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
            await WriteSuccessAsync(console, "Sync completed successfully!");
        }
        else
        {
            await WriteErrorAsync(console, $"Sync failed: {result.ErrorMessage}");
        }

        await console.Output.WriteLineAsync();
        await console.Output.WriteLineAsync($"Messages synced:  {result.MessagesSynced}");
        await console.Output.WriteLineAsync($"Messages skipped: {result.MessagesSkipped}");
        await console.Output.WriteLineAsync($"Folders processed: {result.FoldersProcessed}");

        if (result.Errors > 0)
        {
            await WriteWarningAsync(console, $"Errors: {result.Errors}");
        }

        await console.Output.WriteLineAsync($"Elapsed time: {result.Elapsed:hh\\:mm\\:ss}");
    }
}
