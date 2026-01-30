using CliFx.Attributes;
using CliFx.Infrastructure;
using M365MailMirror.Core.Configuration;
using M365MailMirror.Core.Exceptions;
using M365MailMirror.Core.Logging;
using M365MailMirror.Cli.Services;

namespace M365MailMirror.Cli.Commands;

/// <summary>
/// Exports embedded resources (CLAUDE.md, skills, etc.) to the output directory.
/// </summary>
[Command("export-resources", Description = "Export embedded resources (CLAUDE.md, skills) to the output directory")]
public class ExportResourcesCommand : BaseCommand
{
    [CommandOption("config", 'c', Description = "Path to configuration file (searches ./config.yaml, then ~/.config/m365-mail-mirror/config.yaml)")]
    public string? ConfigPath { get; init; }

    [CommandOption("archive", 'a', Description = "Path to the output directory (defaults to config OutputPath, which defaults to current directory)")]
    public string? ArchivePath { get; init; }

    [CommandOption("overwrite", Description = "Overwrite existing files (default: false)")]
    public bool Overwrite { get; init; }

    [CommandOption("verbose", 'v', Description = "Enable verbose logging")]
    public bool Verbose { get; init; }

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        ConfigureLogging(console, Verbose);
        var logger = LoggerFactory.CreateLogger<ExportResourcesCommand>();
        var cancellationToken = console.RegisterCancellationHandler();

        // Load configuration
        var config = ConfigurationLoader.Load(ConfigPath);
        var outputPath = ArchivePath ?? config.OutputPath;

        // Verify output directory exists
        if (!Directory.Exists(outputPath))
        {
            throw new M365MailMirrorException(
                $"Output directory does not exist: {outputPath}",
                CliExitCodes.FileSystemError);
        }

        // Export resources
        var exported = ResourceExtractor.ExportAll(outputPath, Overwrite);

        // Report results
        if (exported.Count == 0)
        {
            await console.Output.WriteLineAsync("No resources available to export.");
            return;
        }

        await WriteSuccessAsync(console, $"Exported {exported.Count} resource(s) to: {outputPath}");

        foreach (var resource in exported)
        {
            var statusText = resource.Status switch
            {
                ExportStatus.Created => $"Created: {resource.RelativePath}",
                ExportStatus.Overwritten => $"Overwrote: {resource.RelativePath}",
                ExportStatus.Skipped => $"Skipped: {resource.RelativePath} (already exists)",
                _ => $"Unknown status: {resource.RelativePath}"
            };
            await console.Output.WriteLineAsync($"  {statusText}");
        }

        // Update PATH in settings.local.json with the tool's directory
        try
        {
            var toolDirectory = PathConfigurationService.GetToolDirectory();
            var pathEntry = PathConfigurationService.GeneratePathEntry(toolDirectory);
            PathConfigurationService.UpdateSettingsLocalJson(outputPath, pathEntry);
            await console.Output.WriteLineAsync($"Updated PATH in .claude/settings.local.json to include: {toolDirectory}");
            logger.Info($"Successfully updated PATH configuration in settings.local.json with tool directory: {toolDirectory}");
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to update PATH configuration: {ex.Message}");
            await console.Output.WriteLineAsync($"Warning: Could not update PATH configuration: {ex.Message}");
        }
    }
}
