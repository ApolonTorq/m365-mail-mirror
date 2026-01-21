using CliFx.Attributes;
using CliFx.Infrastructure;
using M365MailMirror.Core.Configuration;
using M365MailMirror.Core.Logging;
using M365MailMirror.Infrastructure.Authentication;

namespace M365MailMirror.Cli.Commands;

[Command("auth status", Description = "Show current authentication status")]
public class AuthStatusCommand : BaseCommand
{
    [CommandOption("config", 'c', Description = "Path to configuration file")]
    public string? ConfigPath { get; init; }

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        var logger = LoggerFactory.CreateLogger<AuthStatusCommand>();

        // Load configuration
        var config = ConfigurationLoader.Load(ConfigPath);

        await console.Output.WriteLineAsync("Authentication Status");
        await console.Output.WriteLineAsync("=====================");
        await console.Output.WriteLineAsync();

        if (string.IsNullOrEmpty(config.ClientId))
        {
            await console.Output.WriteLineAsync("Client ID:    (not configured)");
            await console.Output.WriteLineAsync();
            await console.Output.WriteLineAsync("Note: Set client ID via config file, --client-id option, or M365_MAIL_MIRROR_CLIENT_ID env var.");
            return;
        }

        await console.Output.WriteLineAsync($"Client ID:    {config.ClientId}");
        await console.Output.WriteLineAsync($"Tenant ID:    {config.TenantId}");
        await console.Output.WriteLineAsync();

        var tokenCache = new FileTokenCacheStorage();
        var authService = new MsalAuthenticationService(config.ClientId, config.TenantId, tokenCache, logger);
        var status = await authService.GetStatusAsync();

        await console.Output.WriteLineAsync($"Cache:        {status.CacheLocation}");
        await console.Output.WriteLineAsync($"Has Cache:    {(status.HasCachedToken ? "Yes" : "No")}");
        await console.Output.WriteLineAsync();

        if (status.IsAuthenticated)
        {
            await WriteSuccessAsync(console, "Status:       Authenticated");
            await console.Output.WriteLineAsync($"Account:      {status.Account}");

            if (!string.IsNullOrEmpty(status.TenantId))
            {
                await console.Output.WriteLineAsync($"Tenant:       {status.TenantId}");
            }
        }
        else if (status.HasCachedToken)
        {
            await WriteWarningAsync(console, "Status:       Token expired");

            if (!string.IsNullOrEmpty(status.Account))
            {
                await console.Output.WriteLineAsync($"Account:      {status.Account}");
            }

            await console.Output.WriteLineAsync();
            await console.Output.WriteLineAsync("Run 'auth login' to re-authenticate.");
        }
        else
        {
            await WriteWarningAsync(console, "Status:       Not authenticated");
            await console.Output.WriteLineAsync();
            await console.Output.WriteLineAsync("Run 'auth login' to authenticate.");
        }
    }
}
