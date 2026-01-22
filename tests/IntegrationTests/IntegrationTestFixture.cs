using System.Reflection;
using System.Text;
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
public class IntegrationTestFixture : IAsyncLifetime, IDisposable
{
    private bool _disposed;
    private static readonly string ProjectRoot = FindProjectRoot();
    private static readonly string ConfigPath = Path.Combine(ProjectRoot, "config.yaml");
    private static readonly string TestOutputRoot = Path.Combine(ProjectRoot, "tests", "IntegrationTests", "output");

    private StreamWriter? _logFileWriter;
    private TeeTextWriter? _teeWriter;

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
    /// The log file stream for capturing all test output.
    /// Available after InitializeAsync() completes when a log file is configured.
    /// </summary>
    public Stream? LogFileStream => _logFileWriter?.BaseStream;

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

    /// <summary>
    /// The test settings loaded from integration-test-settings.json.
    /// </summary>
    public TestSettings Settings { get; private set; } = null!;

    /// <summary>
    /// The configured log level from integration-test-settings.json.
    /// </summary>
    public AppLogLevel LogLevel => Settings?.LogLevel ?? AppLogLevel.Info;

    /// <summary>
    /// Whether verbose (debug) logging is enabled.
    /// </summary>
    public bool IsVerbose => LogLevel == AppLogLevel.Debug;

    public async Task InitializeAsync()
    {
        // Clear previous test results
        TestResultTracker.Clear();

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

        // Load test settings from integration-test-settings.json
        Settings = TestSettings.Load(ProjectRoot);

        // Set up file logging if configured
        if (!string.IsNullOrEmpty(Settings.LogFileName))
        {
            var statusDir = Path.Combine(TestOutputPath, "status");
            Directory.CreateDirectory(statusDir);

            var logFilePath = Path.Combine(statusDir, Settings.LogFileName);
            _logFileWriter = new StreamWriter(logFilePath, append: false, Encoding.UTF8) { AutoFlush = true };
            _teeWriter = new TeeTextWriter(Console.Out, _logFileWriter);

            // Configure logging with tee writer for both output and error
            LoggerFactory.Configure(minimumLevel: Settings.LogLevel, output: _teeWriter, error: _teeWriter);
        }
        else
        {
            // Configure logging with console only
            LoggerFactory.Configure(minimumLevel: Settings.LogLevel);
        }

        // Load configuration from project root config.yaml
        Config = ConfigurationLoader.Load(ConfigPath);

        // Create logger
        Logger = LoggerFactory.CreateLogger<IntegrationTestFixture>();

        Logger.Info("Test settings loaded: LogLevel={0}", Settings.LogLevel);

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

                // Use ForwardingTestConsole to capture output AND forward to real console and log file
                using var console = new ForwardingTestConsole(forwardToConsole: true, logFileStream: LogFileStream);
                var syncCommand = new SyncCommand
                {
                    ConfigPath = ConfigFilePath,
                    OutputPath = TestOutputPath,
                    CheckpointInterval = 10,
                    Parallel = 2,
                    Verbose = IsVerbose, // Use configured log level from integration-test-settings.json
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
        // Output test summary
        OutputTestSummary();

        // Dispose resources
        Dispose();

        // Don't delete the output folder - keep it for inspection after test runs
        // The folder is cleared at the start of each test run instead
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _teeWriter?.Dispose();
        _logFileWriter?.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Outputs a summary of all tests in the collection with their descriptions.
    /// Writes to both console (with colors) and log file (plain text).
    /// </summary>
    private void OutputTestSummary()
    {
        WriteLineToAll("");
        WriteLineToAll(new string('=', 80));
        WriteLineToAll("INTEGRATION TEST SUMMARY");
        WriteLineToAll(new string('=', 80));
        WriteLineToAll("");

        // Discover all test classes in this collection via reflection
        // Note: xUnit's CollectionAttribute stores the name as a constructor argument, not a property
        var testAssembly = typeof(IntegrationTestFixture).Assembly;
        var testClasses = testAssembly.GetTypes()
            .Where(t => t.GetCustomAttributesData()
                .Any(a => a.AttributeType == typeof(CollectionAttribute) &&
                          a.ConstructorArguments.Count > 0 &&
                          a.ConstructorArguments[0].Value is string name &&
                          name == "IntegrationTests"))
            .OrderBy(t => t.Name)
            .ToList();

        var totalTests = 0;
        var testsByClass = new List<(string ClassName, List<(string MethodName, string? Description)> Tests)>();

        foreach (var testClass in testClasses)
        {
            var testMethods = testClass.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttributes().Any(a =>
                    a.GetType().Name == "SkippableFactAttribute" ||
                    a.GetType().Name == "FactAttribute" ||
                    a.GetType().Name == "TheoryAttribute"))
                .OrderBy(m => m.Name)
                .ToList();

            var tests = new List<(string MethodName, string? Description)>();
            foreach (var method in testMethods)
            {
                var descAttr = method.GetCustomAttribute<TestDescriptionAttribute>();
                tests.Add((method.Name, descAttr?.Description));
                totalTests++;
            }

            if (tests.Count > 0)
            {
                testsByClass.Add((testClass.Name, tests));
            }
        }

        // Get result summary
        var (tracked, passed, failed, skipped) = TestResultTracker.GetSummary();

        // Build summary line for log file
        var summaryLine = $"Tests in collection: {totalTests}  |  Tracked: {tracked}  |  Passed: {passed}  |  Failed: {failed}  |  Skipped: {skipped}";
        _logFileWriter?.WriteLine(summaryLine);

        // Write colorized version to console
        Console.Write($"Tests in collection: {totalTests}  |  Tracked: {tracked}  |  ");
        WriteColored("Passed: ", ConsoleColor.White);
        WriteColored($"{passed}", ConsoleColor.Green);
        Console.Write("  |  ");
        WriteColored("Failed: ", ConsoleColor.White);
        WriteColored($"{failed}", failed > 0 ? ConsoleColor.Red : ConsoleColor.White);
        Console.Write("  |  ");
        WriteColored("Skipped: ", ConsoleColor.White);
        WriteColored($"{skipped}", skipped > 0 ? ConsoleColor.Yellow : ConsoleColor.White);
        Console.WriteLine();
        WriteLineToAll("");

        foreach (var (className, tests) in testsByClass)
        {
            WriteLineToAll($"  {className}:");

            // Derive the prefix to strip from method names (e.g., "StatusCommand_" from "StatusCommandIntegrationTests")
            var commandPrefix = className.EndsWith("IntegrationTests", StringComparison.Ordinal)
                ? className[..^"IntegrationTests".Length] + "_"
                : "";

            foreach (var (methodName, description) in tests)
            {
                var result = TestResultTracker.GetResult(className, methodName);
                var statusPrefix = GetStatusPrefix(result);
                var statusColor = GetStatusColor(result);

                // Strip the command prefix from the method name for cleaner display
                var displayName = methodName.StartsWith(commandPrefix, StringComparison.Ordinal)
                    ? methodName[commandPrefix.Length..]
                    : methodName;

                var testLine = string.IsNullOrEmpty(description)
                    ? displayName
                    : $"{displayName}: {description}";

                // Write plain text to log file
                _logFileWriter?.WriteLine($"    {statusPrefix}{testLine}");

                // Write colorized to console
                Console.Write("    ");
                WriteColored(statusPrefix, statusColor);
                Console.WriteLine(testLine);
            }
            WriteLineToAll("");
        }
    }

    /// <summary>
    /// Writes a line to both console and log file.
    /// </summary>
    private void WriteLineToAll(string text)
    {
        Console.WriteLine(text);
        _logFileWriter?.WriteLine(text);
    }

    /// <summary>
    /// Gets the status prefix for a test result (e.g., "[PASS] ", "[FAIL] ").
    /// </summary>
    private static string GetStatusPrefix(TestResult? result)
    {
        if (result == null)
            return "[????] ";
        if (result.Skipped)
            return "[SKIP] ";
        return result.Passed ? "[PASS] " : "[FAIL] ";
    }

    /// <summary>
    /// Gets the console color for a test result status.
    /// </summary>
    private static ConsoleColor GetStatusColor(TestResult? result)
    {
        if (result == null)
            return ConsoleColor.DarkGray;
        if (result.Skipped)
            return ConsoleColor.Yellow;
        return result.Passed ? ConsoleColor.Green : ConsoleColor.Red;
    }

    /// <summary>
    /// Writes text to the console in a specific color using ANSI escape codes.
    /// ANSI codes work even when output is redirected (e.g., under dotnet test).
    /// </summary>
    private static void WriteColored(string text, ConsoleColor color)
    {
        var ansiCode = color switch
        {
            ConsoleColor.Green => "\x1b[32m",
            ConsoleColor.Red => "\x1b[31m",
            ConsoleColor.Yellow => "\x1b[33m",
            ConsoleColor.DarkGray => "\x1b[90m",
            ConsoleColor.White => "\x1b[97m",
            _ => "\x1b[0m"
        };
        Console.Write($"{ansiCode}{text}\x1b[0m");
    }

    /// <summary>
    /// Creates a database initialized for testing at the test output path.
    /// </summary>
    public async Task<StateDatabase> CreateDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var statusDir = Path.Combine(TestOutputPath, StateDatabase.DatabaseDirectory);
        Directory.CreateDirectory(statusDir);
        var databasePath = Path.Combine(statusDir, StateDatabase.DefaultDatabaseFilename);
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
        return Path.Combine(TestOutputPath, StateDatabase.DatabaseDirectory, StateDatabase.DefaultDatabaseFilename);
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

    /// <summary>
    /// A TextWriter that writes to multiple underlying writers (tee pattern).
    /// </summary>
    private sealed class TeeTextWriter : TextWriter
    {
        private readonly TextWriter[] _writers;

        public TeeTextWriter(params TextWriter[] writers)
        {
            _writers = writers;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            foreach (var writer in _writers)
            {
                writer.Write(value);
            }
        }

        public override void Write(string? value)
        {
            foreach (var writer in _writers)
            {
                writer.Write(value);
            }
        }

        public override void WriteLine(string? value)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(value);
            }
        }

        public override void Flush()
        {
            foreach (var writer in _writers)
            {
                writer.Flush();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Flush();
            }
            base.Dispose(disposing);
        }
    }
}
