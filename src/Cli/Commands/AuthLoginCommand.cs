using CliFx.Attributes;
using CliFx.Infrastructure;
using M365MailMirror.Core.Configuration;
using M365MailMirror.Core.Exceptions;
using M365MailMirror.Core.Logging;
using M365MailMirror.Infrastructure.Authentication;

namespace M365MailMirror.Cli.Commands;

[Command("auth login", Description = "Authenticate with Microsoft 365 using device code flow")]
public class AuthLoginCommand : BaseCommand
{
    [CommandOption("tenant", 't', Description = "Azure AD tenant ID (default: common)")]
    public string? TenantId { get; init; }

    [CommandOption("client-id", Description = "Azure AD application client ID")]
    public string? ClientId { get; init; }

    [CommandOption("config", 'c', Description = "Path to configuration file (searches ./config.yaml, then ~/.config/m365-mail-mirror/config.yaml)")]
    public string? ConfigPath { get; init; }

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        ConfigureLogging(console);
        var logger = LoggerFactory.CreateLogger<AuthLoginCommand>();

        // Load configuration
        var config = ConfigurationLoader.Load(ConfigPath);
        config = ConfigurationLoader.MergeCommandLineOverrides(config, ClientId, TenantId);

        if (string.IsNullOrEmpty(config.ClientId))
        {
            await console.Error.WriteLineAsync("Error: Client ID is required.");
            await console.Error.WriteLineAsync("Provide it via --client-id option, config file, or M365_MAIL_MIRROR_CLIENT_ID environment variable.");
            await console.Error.WriteLineAsync();
            await console.Error.WriteLineAsync("To register an Azure AD application:");
            await console.Error.WriteLineAsync("  1. Go to https://portal.azure.com");
            await console.Error.WriteLineAsync("  2. Navigate to Azure Active Directory > App registrations");
            await console.Error.WriteLineAsync("  3. Click 'New registration'");
            await console.Error.WriteLineAsync("  4. Set redirect URI to 'http://localhost' (type: Public client/native)");
            await console.Error.WriteLineAsync("  5. Add 'Mail.ReadWrite' delegated permission under API permissions");
            throw new ConfigurationException("Client ID is required.");
        }

        await console.Output.WriteLineAsync("Starting device code authentication...");
        await console.Output.WriteLineAsync();

        var tokenCache = new FileTokenCacheStorage();
        var authService = new MsalAuthenticationService(config.ClientId, config.TenantId, tokenCache, logger);

        var result = await authService.AuthenticateWithDeviceCodeAsync(deviceCodeInfo =>
        {
            console.Output.WriteLine($"To sign in, open a browser and go to:");
            console.Output.WriteLine();
            console.Output.WriteLine($"  {deviceCodeInfo.VerificationUrl}");
            console.Output.WriteLine();
            console.Output.WriteLine($"Then enter the code: {deviceCodeInfo.UserCode}");
            console.Output.WriteLine();
            console.Output.WriteLine("Waiting for authentication...");
        }, console.RegisterCancellationHandler());

        if (result.IsSuccess)
        {
            await console.Output.WriteLineAsync();
            await WriteSuccessAsync(console, $"Successfully authenticated as: {result.Account}");
            await console.Output.WriteLineAsync($"Token expires at: {result.ExpiresOn:yyyy-MM-dd HH:mm:ss}");
        }
        else
        {
            throw new M365MailMirrorException($"Authentication failed: {result.ErrorMessage}", CliExitCodes.AuthenticationError);
        }
    }
}
