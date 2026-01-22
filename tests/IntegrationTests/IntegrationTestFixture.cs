using M365MailMirror.Cli.Commands;
using M365MailMirror.Core.Configuration;
using M365MailMirror.Core.Logging;
using M365MailMirror.Infrastructure.Authentication;
using M365MailMirror.Infrastructure.Database;

namespace M365MailMirror.IntegrationTests;

/// <summary>
/// Shared integration test fixture providing authenticated Graph client and test infrastructure.
/// This fixture is shared across all tests in the "IntegrationTests" collection via ICollectionFixture.
/// Authentication and initial sync happen only once for all tests.
/// </summary>
public class IntegrationTestFixture : IAsyncLifetime
{
    private static readonly string ProjectRoot = FindProjectRoot();
    private static readonly string ConfigPath = Path.Combine(ProjectRoot, "config.yaml");
    private static readonly string TestOutputRoot = Path.Combine(ProjectRoot, "tests", "IntegrationTests", "output");

    /// <summary>
    /// The loaded application configuration.
    /// </summary>
    public AppConfiguration Config { get; private set; } = null!;

    /// <summary>
    /// The unique test output path for this fixture instance.
    /// </summary>
    public string TestOutputPath { get; private set; } = null!;

    /// <summary>
    /// The path to the config.yaml file in the project root.
    /// </summary>
    public string ConfigFilePath => ConfigPath;

    /// <summary>
    /// Logger instance for test operations.
    /// </summary>
    public IAppLogger Logger { get; private set; } = null!;

    /// <summary>
    /// Whether the user is authenticated (has valid token from 'auth login').
    /// </summary>
    public bool IsAuthenticated { get; private set; }

    /// <summary>
    /// The account email that is authenticated, if any.
    /// </summary>
    public string? AuthenticatedAccount { get; private set; }

    /// <summary>
    /// Whether the initial sync has been completed.
    /// </summary>
    public bool InitialSyncCompleted { get; private set; }

    public async Task InitializeAsync()
    {
        // Use the output folder directly, clearing previous contents
        TestOutputPath = TestOutputRoot;

        // Clear previous test output if it exists
        if (Directory.Exists(TestOutputPath))
        {
            foreach (var file in Directory.GetFiles(TestOutputPath))
                File.Delete(file);
            foreach (var dir in Directory.GetDirectories(TestOutputPath))
                Directory.Delete(dir, recursive: true);
        }
        else
        {
            Directory.CreateDirectory(TestOutputPath);
        }

        // Load configuration from project root config.yaml
        Config = ConfigurationLoader.Load(ConfigPath);

        // Create logger
        Logger = LoggerFactory.CreateLogger<IntegrationTestFixture>();

        // Check authentication status by verifying token cache exists
        // We don't call AcquireTokenSilent here because it can trigger AAD throttling
        // when running multiple tests. The actual token validity is checked when
        // commands execute. If the cache exists, we assume the user has authenticated.
        if (!string.IsNullOrEmpty(Config.ClientId))
        {
            try
            {
                var tokenCache = new FileTokenCacheStorage();
                var hasCache = await tokenCache.ExistsAsync();

                if (hasCache)
                {
                    // Read the cache to verify it's valid and extract account info
                    // This doesn't make any network calls - it just reads the local file
                    var cacheData = await tokenCache.ReadAsync();
                    IsAuthenticated = cacheData != null && cacheData.Length > 0;

                    // Note: We can't easily extract the account name without MSAL parsing,
                    // but the important thing is knowing we have cached credentials
                    AuthenticatedAccount = IsAuthenticated ? "(cached credentials available)" : null;
                }
                else
                {
                    IsAuthenticated = false;
                }
            }
            catch
            {
                // If cache read fails, we're not authenticated
                IsAuthenticated = false;
            }
        }

        // Run initial sync to populate test data for all tests
        // This runs once for all tests in the collection
        if (IsAuthenticated)
        {
            await RunInitialSyncAsync();
        }
    }

    /// <summary>
    /// Runs the initial sync to populate test data.
    /// Called once during fixture initialization.
    /// </summary>
    private async Task RunInitialSyncAsync()
    {
        const int maxRetries = 3;
        const int baseDelaySeconds = 30;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                Logger.Info("Running initial sync for integration tests (attempt {0}/{1})...", attempt, maxRetries);

                // Use ForwardingTestConsole to capture output AND forward to real console in real-time
                using var console = new ForwardingTestConsole();
                var syncCommand = new SyncCommand
                {
                    ConfigPath = ConfigFilePath,
                    OutputPath = TestOutputPath,
                    CheckpointInterval = 10,
                    Parallel = 2,
                    Verbose = true, // Enable verbose logging for integration tests
                    GenerateHtml = true, // Enable inline HTML transformation during sync
                    ExtractAttachments = true // Enable inline attachment extraction during sync
                    // Note: GenerateMarkdown is left false - it's tested separately via TransformCommand
                };

                await syncCommand.ExecuteAsync(console);

                // Verify sync actually worked by checking for EML files
                var emlDir = Path.Combine(TestOutputPath, "eml");
                if (Directory.Exists(emlDir) && Directory.GetFiles(emlDir, "*.eml", SearchOption.AllDirectories).Length > 0)
                {
                    InitialSyncCompleted = true;
                    Logger.Info("Initial sync completed successfully");
                    return;
                }

                Logger.Warning("Sync completed but no EML files found - may have been throttled");
            }
            catch (Exception ex)
            {
                var isThrottling = ex.Message.Contains("throttled", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("too many requests", StringComparison.OrdinalIgnoreCase);

                if (isThrottling && attempt < maxRetries)
                {
                    var delay = baseDelaySeconds * attempt;
                    Logger.Warning("AAD throttling detected. Waiting {0} seconds before retry...", delay);
                    await Task.Delay(TimeSpan.FromSeconds(delay));
                    continue;
                }

                Logger.Error(ex, "Initial sync failed: {0}", ex.Message);
                InitialSyncCompleted = false;
                return;
            }
        }

        InitialSyncCompleted = false;
    }

    public Task DisposeAsync()
    {
        // Don't delete the output folder - keep it for inspection after test runs
        // The folder is cleared at the start of each test run instead
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates a database initialized for testing at the test output path.
    /// </summary>
    public async Task<StateDatabase> CreateDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var databasePath = Path.Combine(TestOutputPath, StateDatabase.DefaultDatabaseFilename);
        var database = new StateDatabase(databasePath, Logger);
        await database.InitializeAsync(cancellationToken);
        return database;
    }

    /// <summary>
    /// Skips the test if not authenticated.
    /// Call this at the beginning of tests that require authentication.
    /// Uses Xunit.SkippableFact's Skip.If to properly skip the test.
    /// </summary>
    /// <remarks>
    /// Tests calling this method must use [SkippableFact] instead of [Fact].
    /// </remarks>
    public void SkipIfNotAuthenticated()
    {
        Skip.If(!IsAuthenticated,
            "Not authenticated. Run 'auth login' to authenticate before running integration tests.");
    }

    /// <summary>
    /// Gets the path to the database file in the test output directory.
    /// </summary>
    public string GetDatabasePath()
    {
        return Path.Combine(TestOutputPath, StateDatabase.DefaultDatabaseFilename);
    }

    /// <summary>
    /// Creates an isolated test directory for tests that need clean state.
    /// The directory is created fresh (deleted if exists) to ensure test isolation.
    /// </summary>
    /// <param name="testName">Unique name for this test's isolated directory.</param>
    /// <returns>Path to the isolated test directory.</returns>
    public string CreateIsolatedTestDirectory(string testName)
    {
        var isolatedPath = Path.Combine(TestOutputPath, "_isolated", testName);

        if (Directory.Exists(isolatedPath))
        {
            Directory.Delete(isolatedPath, recursive: true);
        }

        Directory.CreateDirectory(isolatedPath);
        return isolatedPath;
    }

    /// <summary>
    /// Finds the project root by walking up the directory tree looking for config.yaml.
    /// </summary>
    private static string FindProjectRoot()
    {
        var directory = Directory.GetCurrentDirectory();

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "config.yaml")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not find project root (looking for config.yaml). " +
            "Make sure the config.yaml file exists in the project root directory.");
    }
}
