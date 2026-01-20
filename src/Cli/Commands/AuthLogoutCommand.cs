using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using M365MailMirror.Core.Configuration;
using M365MailMirror.Core.Logging;
using M365MailMirror.Infrastructure.Authentication;

namespace M365MailMirror.Cli.Commands;

[Command("auth logout", Description = "Clear stored authentication tokens")]
public class AuthLogoutCommand : ICommand
{
    [CommandOption("config", 'c', Description = "Path to configuration file")]
    public string? ConfigPath { get; init; }

    public async ValueTask ExecuteAsync(IConsole console)
    {
        var logger = LoggerFactory.CreateLogger<AuthLogoutCommand>();

        // Load configuration
        var config = ConfigurationLoader.Load(ConfigPath);

        if (string.IsNullOrEmpty(config.ClientId))
        {
            // Even without client ID, we can still clear the token cache
            await console.Output.WriteLineAsync("Clearing token cache...");
        }
        else
        {
            await console.Output.WriteLineAsync($"Signing out (tenant: {config.TenantId})...");
        }

        try
        {
            var tokenCache = new FileTokenCacheStorage();

            // If we have a client ID, use the full sign-out flow
            if (!string.IsNullOrEmpty(config.ClientId))
            {
                var authService = new MsalAuthenticationService(config.ClientId, config.TenantId, tokenCache, logger);
                await authService.SignOutAsync();
            }
            else
            {
                // Just clear the cache directly
                await tokenCache.ClearAsync();
            }

            console.ForegroundColor = ConsoleColor.Green;
            await console.Output.WriteLineAsync("Successfully signed out and cleared cached tokens.");
            console.ResetColor();
        }
        catch (Exception ex)
        {
            console.ForegroundColor = ConsoleColor.Red;
            await console.Error.WriteLineAsync($"Error during logout: {ex.Message}");
            console.ResetColor();
            logger.Error(ex, "Error during logout");
        }
    }
}
