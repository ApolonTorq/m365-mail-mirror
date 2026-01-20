using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using M365MailMirror.Core.Configuration;
using M365MailMirror.Core.Logging;
using M365MailMirror.Core.Transform;
using M365MailMirror.Infrastructure.Database;
using M365MailMirror.Infrastructure.Storage;
using M365MailMirror.Infrastructure.Transform;

namespace M365MailMirror.Cli.Commands;

[Command("transform", Description = "Transform EML files to HTML, Markdown, and extract attachments")]
public class TransformCommand : ICommand
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

    [CommandOption("html", Description = "Enable HTML transformation")]
    public bool Html { get; init; } = true;

    [CommandOption("markdown", Description = "Enable Markdown transformation")]
    public bool Markdown { get; init; } = true;

    [CommandOption("attachments", Description = "Enable attachment extraction")]
    public bool Attachments { get; init; } = true;

    public async ValueTask ExecuteAsync(IConsole console)
    {
        var logger = LoggerFactory.CreateLogger<TransformCommand>();
        var cancellationToken = console.RegisterCancellationHandler();

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
                await console.Output.WriteLineAsync("Run 'sync' first to create the archive.");
                console.ResetColor();
                return;
            }

            await console.Output.WriteLineAsync($"Archive: {archiveRoot}");
            await console.Output.WriteLineAsync($"Parallel: {Parallel}");

            if (!string.IsNullOrEmpty(Only))
            {
                await console.Output.WriteLineAsync($"Only: {Only}");
            }

            if (Force)
            {
                console.ForegroundColor = ConsoleColor.Yellow;
                await console.Output.WriteLineAsync("Force mode: regenerating all transformations");
                console.ResetColor();
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
                EnableHtml = Html,
                EnableMarkdown = Markdown,
                EnableAttachments = Attachments
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

            // Display results
            await console.Output.WriteLineAsync();

            if (result.Success)
            {
                console.ForegroundColor = ConsoleColor.Green;
                await console.Output.WriteLineAsync("Transformation completed successfully!");
                console.ResetColor();
            }
            else
            {
                console.ForegroundColor = ConsoleColor.Red;
                await console.Output.WriteLineAsync($"Transformation failed: {result.ErrorMessage}");
                console.ResetColor();
            }

            await console.Output.WriteLineAsync();
            await console.Output.WriteLineAsync($"Messages transformed: {result.MessagesTransformed}");
            await console.Output.WriteLineAsync($"Messages skipped:     {result.MessagesSkipped}");

            if (result.Errors > 0)
            {
                console.ForegroundColor = ConsoleColor.Yellow;
                await console.Output.WriteLineAsync($"Errors:               {result.Errors}");
                console.ResetColor();
            }

            await console.Output.WriteLineAsync($"Elapsed time:         {result.Elapsed:hh\\:mm\\:ss}");
        }
        catch (OperationCanceledException)
        {
            console.ForegroundColor = ConsoleColor.Yellow;
            await console.Output.WriteLineAsync();
            await console.Output.WriteLineAsync("Transformation cancelled by user.");
            console.ResetColor();
        }
        catch (Exception ex)
        {
            console.ForegroundColor = ConsoleColor.Red;
            await console.Error.WriteLineAsync($"Error: {ex.Message}");
            console.ResetColor();
            logger.Error(ex, "Transform failed: {0}", ex.Message);
        }
    }
}
