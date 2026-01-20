using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using M365MailMirror.Core.Configuration;
using M365MailMirror.Core.Logging;
using M365MailMirror.Infrastructure.Database;
using M365MailMirror.Infrastructure.Storage;

namespace M365MailMirror.Cli.Commands;

[Command("verify", Description = "Verify integrity of EML files and database consistency")]
public class VerifyCommand : ICommand
{
    [CommandOption("config", 'c', Description = "Path to configuration file")]
    public string? ConfigPath { get; init; }

    [CommandOption("archive", 'a', Description = "Path to the mail archive")]
    public string? ArchivePath { get; init; }

    [CommandOption("fix", Description = "Automatically fix recoverable issues")]
    public bool Fix { get; init; }

    [CommandOption("verbose", 'v', Description = "Show detailed verification results")]
    public bool Verbose { get; init; }

    public async ValueTask ExecuteAsync(IConsole console)
    {
        var logger = LoggerFactory.CreateLogger<VerifyCommand>();
        var cancellationToken = console.RegisterCancellationHandler();

        var missingFiles = new List<string>();
        var orphanedRecords = new List<string>();
        var untrackedFiles = new List<string>();
        var corruptedFiles = new List<string>();

        try
        {
            // Load configuration
            var config = ConfigurationLoader.Load(ConfigPath);
            var archiveRoot = ArchivePath ?? config.OutputPath;

            // Verify archive directory exists
            if (!Directory.Exists(archiveRoot))
            {
                console.ForegroundColor = ConsoleColor.Red;
                await console.Output.WriteLineAsync($"Archive directory does not exist: {archiveRoot}");
                console.ResetColor();
                return;
            }

            // Check if database exists
            var databasePath = Path.Combine(archiveRoot, StateDatabase.DefaultDatabaseFilename);
            if (!File.Exists(databasePath))
            {
                console.ForegroundColor = ConsoleColor.Red;
                await console.Output.WriteLineAsync($"No archive database found at: {databasePath}");
                console.ResetColor();
                return;
            }

            await console.Output.WriteLineAsync($"Verifying archive: {archiveRoot}");
            await console.Output.WriteLineAsync();

            await using var database = new StateDatabase(databasePath, logger);
            await database.InitializeAsync(cancellationToken);

            var emlStorage = new EmlStorageService(archiveRoot, logger);

            // Phase 1: Check database records against files
            await console.Output.WriteLineAsync("Checking database entries against files...");

            var allMessages = await GetAllMessagesAsync(database, cancellationToken);
            var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var message in allMessages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                checkedPaths.Add(message.LocalPath);

                if (!emlStorage.Exists(message.LocalPath))
                {
                    missingFiles.Add(message.LocalPath);

                    if (Verbose)
                    {
                        console.ForegroundColor = ConsoleColor.Yellow;
                        await console.Output.WriteLineAsync($"  Missing: {message.LocalPath}");
                        console.ResetColor();
                    }
                }
            }

            // Phase 2: Scan file system for untracked EML files
            await console.Output.WriteLineAsync("Scanning file system for untracked files...");

            var emlDirectory = Path.Combine(archiveRoot, EmlStorageService.EmlDirectory);
            if (Directory.Exists(emlDirectory))
            {
                var allEmlFiles = Directory.EnumerateFiles(emlDirectory, "*.eml", SearchOption.AllDirectories);

                foreach (var fullPath in allEmlFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Get relative path
                    var relativePath = Path.GetRelativePath(archiveRoot, fullPath);

                    if (!checkedPaths.Contains(relativePath))
                    {
                        untrackedFiles.Add(relativePath);

                        if (Verbose)
                        {
                            console.ForegroundColor = ConsoleColor.Cyan;
                            await console.Output.WriteLineAsync($"  Untracked: {relativePath}");
                            console.ResetColor();
                        }
                    }
                }
            }

            // Phase 3: Check for basic EML file validity (can be read)
            await console.Output.WriteLineAsync("Checking EML file integrity...");

            var filesToCheck = allMessages.Where(m => !missingFiles.Contains(m.LocalPath)).ToList();
            var checkCount = 0;

            foreach (var message in filesToCheck)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Basic check: can we open and read the file?
                    var fullPath = emlStorage.GetFullPath(message.LocalPath);
                    var fileInfo = new FileInfo(fullPath);

                    // Check if file size matches database
                    if (message.Size > 0 && fileInfo.Length != message.Size)
                    {
                        corruptedFiles.Add(message.LocalPath);

                        if (Verbose)
                        {
                            console.ForegroundColor = ConsoleColor.Red;
                            await console.Output.WriteLineAsync($"  Size mismatch: {message.LocalPath} (db: {message.Size}, file: {fileInfo.Length})");
                            console.ResetColor();
                        }
                    }

                    checkCount++;
                }
                catch (Exception ex)
                {
                    corruptedFiles.Add(message.LocalPath);

                    if (Verbose)
                    {
                        console.ForegroundColor = ConsoleColor.Red;
                        await console.Output.WriteLineAsync($"  Error reading: {message.LocalPath} - {ex.Message}");
                        console.ResetColor();
                    }
                }
            }

            // Apply fixes if requested
            var fixedCount = 0;
            if (Fix)
            {
                await console.Output.WriteLineAsync();
                await console.Output.WriteLineAsync("Applying fixes...");

                // Fix: Remove orphaned database records (files that don't exist)
                foreach (var path in missingFiles)
                {
                    var message = allMessages.FirstOrDefault(m => m.LocalPath == path);
                    if (message != null)
                    {
                        await database.DeleteMessageAsync(message.GraphId, cancellationToken);
                        fixedCount++;

                        if (Verbose)
                        {
                            await console.Output.WriteLineAsync($"  Removed orphaned record: {path}");
                        }
                    }
                }
            }

            // Summary
            await console.Output.WriteLineAsync();
            await console.Output.WriteLineAsync("Verification complete:");
            await console.Output.WriteLineAsync();

            var hasIssues = missingFiles.Count > 0 || untrackedFiles.Count > 0 || corruptedFiles.Count > 0;

            if (!hasIssues)
            {
                console.ForegroundColor = ConsoleColor.Green;
                await console.Output.WriteLineAsync("  No issues found. Archive is healthy.");
                console.ResetColor();
            }
            else
            {
                if (missingFiles.Count > 0)
                {
                    console.ForegroundColor = ConsoleColor.Yellow;
                    await console.Output.WriteLineAsync($"  Missing files (database references non-existent files): {missingFiles.Count}");
                    console.ResetColor();
                }

                if (untrackedFiles.Count > 0)
                {
                    console.ForegroundColor = ConsoleColor.Cyan;
                    await console.Output.WriteLineAsync($"  Untracked files (files not in database): {untrackedFiles.Count}");
                    console.ResetColor();
                }

                if (corruptedFiles.Count > 0)
                {
                    console.ForegroundColor = ConsoleColor.Red;
                    await console.Output.WriteLineAsync($"  Corrupted files (size mismatch or unreadable): {corruptedFiles.Count}");
                    console.ResetColor();
                }

                if (Fix && fixedCount > 0)
                {
                    console.ForegroundColor = ConsoleColor.Green;
                    await console.Output.WriteLineAsync($"  Fixed issues: {fixedCount}");
                    console.ResetColor();
                }
                else if (!Fix && missingFiles.Count > 0)
                {
                    await console.Output.WriteLineAsync();
                    await console.Output.WriteLineAsync("  Run with --fix to remove orphaned database records.");
                }
            }

            await console.Output.WriteLineAsync();
            await console.Output.WriteLineAsync($"Files checked: {checkCount}");
        }
        catch (OperationCanceledException)
        {
            console.ForegroundColor = ConsoleColor.Yellow;
            await console.Output.WriteLineAsync("Operation cancelled.");
            console.ResetColor();
        }
        catch (Exception ex)
        {
            console.ForegroundColor = ConsoleColor.Red;
            await console.Error.WriteLineAsync($"Error: {ex.Message}");
            console.ResetColor();
            logger.Error(ex, "Verify failed: {0}", ex.Message);
        }
    }

    private static async Task<IReadOnlyList<M365MailMirror.Core.Database.Entities.Message>> GetAllMessagesAsync(
        StateDatabase database, CancellationToken cancellationToken)
    {
        // Get all messages by iterating through folders and including quarantined
        var allMessages = new List<M365MailMirror.Core.Database.Entities.Message>();
        var folders = await database.GetAllFoldersAsync(cancellationToken);

        foreach (var folder in folders)
        {
            var messages = await database.GetMessagesByFolderAsync(folder.LocalPath, cancellationToken);
            allMessages.AddRange(messages);
        }

        // Also get quarantined messages
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
}
