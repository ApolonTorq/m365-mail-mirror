using CliFx.Attributes;
using CliFx.Infrastructure;
using M365MailMirror.Core.Configuration;
using M365MailMirror.Core.Exceptions;
using M365MailMirror.Core.Logging;
using M365MailMirror.Core.Sync;
using M365MailMirror.Infrastructure.Authentication;
using M365MailMirror.Infrastructure.Database;
using M365MailMirror.Infrastructure.Graph;
using M365MailMirror.Infrastructure.Storage;
using M365MailMirror.Infrastructure.Sync;
using Microsoft.Graph;

namespace M365MailMirror.Cli.Commands;

[Command("sync", Description = "Download and sync emails from Microsoft 365 mailbox")]
public class SyncCommand : BaseCommand
{
    [CommandOption("config", 'c', Description = "Path to configuration file")]
    public string? ConfigPath { get; init; }

    [CommandOption("output", 'o', Description = "Output directory for the mail archive")]
    public string? OutputPath { get; init; }

    [CommandOption("batch-size", 'b', Description = "Number of messages to process per batch")]
    public int BatchSize { get; init; } = 100;

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
        var logger = LoggerFactory.CreateLogger<SyncCommand>();
        var cancellationToken = console.RegisterCancellationHandler();

        // Load configuration
        var config = ConfigurationLoader.Load(ConfigPath);

        // Apply command-line overrides
        var outputPath = OutputPath ?? config.OutputPath;
        var batchSize = BatchSize > 0 ? BatchSize : config.Sync.BatchSize;
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
        await console.Output.WriteLineAsync($"Batch size: {batchSize}");
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

        var authStatus = await authService.GetStatusAsync(cancellationToken);
        if (!authStatus.IsAuthenticated)
        {
            throw new M365MailMirrorException("Not authenticated. Run 'auth login' first.", CliExitCodes.AuthenticationError);
        }

        // Acquire token silently for Graph API calls
        var tokenResult = await authService.AcquireTokenSilentAsync(cancellationToken);
        if (!tokenResult.IsSuccess)
        {
            throw new M365MailMirrorException(tokenResult.ErrorMessage ?? "Failed to acquire token.", CliExitCodes.AuthenticationError);
        }

        await console.Output.WriteLineAsync($"Authenticated as: {authStatus.Account}");
        await console.Output.WriteLineAsync();

        // Create Graph client using a token credential wrapper
        var tokenCredential = new DelegateTokenCredential(authService);
        var graphClient = new GraphServiceClient(tokenCredential);
        var graphMailClient = new GraphMailClient(graphClient, logger);

        // Create database
        var databasePath = Path.Combine(archiveRoot, StateDatabase.DefaultDatabaseFilename);
        await using var database = new StateDatabase(databasePath, logger);
        await database.InitializeAsync(cancellationToken);

        // Create EML storage
        var emlStorage = new EmlStorageService(archiveRoot, logger);

        // Create sync engine
        var syncEngine = new SyncEngine(graphMailClient, database, emlStorage, logger);

        // Build sync options
        var syncOptions = new SyncOptions
        {
            BatchSize = batchSize,
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

        var result = await syncEngine.SyncAsync(syncOptions, progress =>
        {
            // Update progress display
            if (progress.Phase != lastPhase || progress.CurrentFolder != lastFolder)
            {
                lastPhase = progress.Phase;
                lastFolder = progress.CurrentFolder ?? "";

                var folderInfo = !string.IsNullOrEmpty(progress.CurrentFolder)
                    ? $" - {progress.CurrentFolder}"
                    : "";

                console.Output.WriteLine($"[{progress.Phase}]{folderInfo}");
            }
        }, cancellationToken);

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
