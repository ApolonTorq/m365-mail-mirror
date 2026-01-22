using System.Globalization;
using CliFx.Attributes;
using CliFx.Infrastructure;
using M365MailMirror.Core.Configuration;
using M365MailMirror.Core.Logging;
using M365MailMirror.Infrastructure.Database;
using M365MailMirror.Infrastructure.Storage;

namespace M365MailMirror.Cli.Commands;

[Command("status", Description = "Show archive statistics and sync state")]
public class StatusCommand : BaseCommand
{
    [CommandOption("config", 'c', Description = "Path to configuration file")]
    public string? ConfigPath { get; init; }

    [CommandOption("archive", 'a', Description = "Path to the mail archive")]
    public string? ArchivePath { get; init; }

    [CommandOption("quarantine", 'q', Description = "Show quarantine contents")]
    public bool ShowQuarantine { get; init; }

    [CommandOption("verbose", 'v', Description = "Show detailed statistics")]
    public bool Verbose { get; init; }

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        ConfigureLogging(console, Verbose);
        var logger = LoggerFactory.CreateLogger<StatusCommand>();
        var cancellationToken = console.RegisterCancellationHandler();

        // Load configuration
        var config = ConfigurationLoader.Load(ConfigPath);
        var archiveRoot = ArchivePath ?? config.OutputPath;

        // Verify archive directory exists
        if (!Directory.Exists(archiveRoot))
        {
            await WriteWarningAsync(console, $"Archive directory does not exist: {archiveRoot}");
            return;
        }

        // Check if database exists
        var databasePath = Path.Combine(archiveRoot, StateDatabase.DefaultDatabaseFilename);
        if (!File.Exists(databasePath))
        {
            await WriteWarningAsync(console, $"No archive database found at: {databasePath}");
            await console.Output.WriteLineAsync("Run 'sync' to initialize the archive.");
            return;
        }

        await using var database = new StateDatabase(databasePath, logger);
        await database.InitializeAsync(cancellationToken);

        // Get sync state (all mailboxes)
        var syncStates = await database.GetAllSyncStatesAsync(cancellationToken);

        if (syncStates.Count == 0)
        {
            await WriteWarningAsync(console, "No sync history found. Run 'sync' to initialize the archive.");
            return;
        }

        // Display each mailbox's status
        foreach (var syncState in syncStates)
        {
            await console.Output.WriteLineAsync($"Mailbox: {syncState.Mailbox}");
            await console.Output.WriteLineAsync($"Last sync: {syncState.LastSyncTime:yyyy-MM-dd HH:mm:ss}");
            await console.Output.WriteLineAsync();
        }

        // Get statistics
        var messageCount = await database.GetMessageCountAsync(cancellationToken);
        var quarantinedMessages = await database.GetQuarantinedMessagesAsync(cancellationToken);
        var folders = await database.GetAllFoldersAsync(cancellationToken);

        // Calculate sizes
        var emlStorage = new EmlStorageService(archiveRoot, logger);
        long totalSize = 0;
        long quarantineSize = 0;
        var nonQuarantinedCount = 0;

        var allMessages = await GetAllMessagesAsync(database, cancellationToken);
        foreach (var message in allMessages)
        {
            if (message.QuarantinedAt.HasValue)
            {
                // Calculate quarantine size separately
                if (emlStorage.Exists(message.LocalPath))
                {
                    try
                    {
                        quarantineSize += emlStorage.GetFileSize(message.LocalPath);
                    }
                    catch
                    {
                        // File might not exist
                    }
                }
            }
            else
            {
                nonQuarantinedCount++;
                if (emlStorage.Exists(message.LocalPath))
                {
                    try
                    {
                        totalSize += emlStorage.GetFileSize(message.LocalPath);
                    }
                    catch
                    {
                        // File might not exist
                    }
                }
            }
        }

        await console.Output.WriteLineAsync($"Messages: {nonQuarantinedCount:N0} ({FormatSize(totalSize)})");
        await console.Output.WriteLineAsync($"Folders: {folders.Count:N0}");
        await console.Output.WriteLineAsync($"Quarantine: {quarantinedMessages.Count:N0} messages ({FormatSize(quarantineSize)})");

        // Display transformations (count how many messages have each type)
        await console.Output.WriteLineAsync();
        await console.Output.WriteLineAsync("Transformations:");

        var htmlCount = await database.GetTransformationCountByTypeAsync("html", cancellationToken);
        var markdownCount = await database.GetTransformationCountByTypeAsync("markdown", cancellationToken);
        var attachmentCount = await database.GetTransformationCountByTypeAsync("attachments", cancellationToken);

        await console.Output.WriteLineAsync($"  HTML: {htmlCount:N0} messages{(htmlCount == 0 ? " (not generated)" : "")}");
        await console.Output.WriteLineAsync($"  Markdown: {markdownCount:N0} messages{(markdownCount == 0 ? " (not generated)" : "")}");
        await console.Output.WriteLineAsync($"  Attachments: {attachmentCount:N0} messages{(attachmentCount == 0 ? " (not extracted)" : "")}");

        // Show quarantine details if requested
        if (ShowQuarantine && quarantinedMessages.Count > 0)
        {
            await console.Output.WriteLineAsync();
            await console.Output.WriteLineAsync("Quarantined messages:");
            await console.Output.WriteLineAsync();

            foreach (var msg in quarantinedMessages.Take(Verbose ? 100 : 10))
            {
                await console.Output.WriteLineAsync($"  [{msg.QuarantinedAt:yyyy-MM-dd}] {msg.Subject ?? "(no subject)"} - {msg.QuarantineReason}");
            }

            if (!Verbose && quarantinedMessages.Count > 10)
            {
                await console.Output.WriteLineAsync($"  ... and {quarantinedMessages.Count - 10} more (use --verbose to show all)");
            }
        }

        // Verbose: show folder details
        if (Verbose)
        {
            await console.Output.WriteLineAsync();
            await console.Output.WriteLineAsync("Folder details:");
            await console.Output.WriteLineAsync();

            foreach (var folder in folders.OrderBy(f => f.LocalPath))
            {
                var folderMessageCount = await database.GetMessageCountByFolderAsync(folder.LocalPath, cancellationToken);
                var lastSync = folder.LastSyncTime?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "Never";
                await console.Output.WriteLineAsync($"  {folder.LocalPath}: {folderMessageCount:N0} messages (last sync: {lastSync})");
            }
        }
    }

    private static async Task<IReadOnlyList<M365MailMirror.Core.Database.Entities.Message>> GetAllMessagesAsync(
        StateDatabase database, CancellationToken cancellationToken)
    {
        // Get all messages by iterating through folders
        // This is a temporary workaround - ideally we'd have a GetAllMessagesAsync method
        var allMessages = new List<M365MailMirror.Core.Database.Entities.Message>();
        var folders = await database.GetAllFoldersAsync(cancellationToken);

        foreach (var folder in folders)
        {
            var messages = await database.GetMessagesByFolderAsync(folder.LocalPath, cancellationToken);
            allMessages.AddRange(messages);
        }

        // Also get quarantined messages (they might have a different folder path)
        var quarantined = await database.GetQuarantinedMessagesAsync(cancellationToken);
        foreach (var q in quarantined)
        {
            if (!allMessages.Any(m => m.GraphId == q.GraphId))
            {
                allMessages.Add(q);
            }
        }

        return allMessages;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }
}
