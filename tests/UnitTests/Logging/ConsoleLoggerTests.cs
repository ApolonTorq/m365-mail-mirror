using M365MailMirror.Core.Logging;

namespace M365MailMirror.UnitTests.Logging;

public class ConsoleLoggerTests
{
    [Fact]
    public void Constructor_WithDefaultLevel_SetsInfoLevel()
    {
        var logger = new ConsoleLogger();

        logger.MinimumLevel.Should().Be(AppLogLevel.Info);
    }

    [Fact]
    public void Constructor_WithSpecifiedLevel_SetsLevel()
    {
        var logger = new ConsoleLogger(AppLogLevel.Debug);

        logger.MinimumLevel.Should().Be(AppLogLevel.Debug);
    }

    [Fact]
    public void Info_WithInfoLevel_WritesToOutput()
    {
        var output = new StringWriter();
        var errorOutput = new StringWriter();
        var logger = new ConsoleLogger(AppLogLevel.Info, null, output, errorOutput);

        logger.Info("Test message");

        output.ToString().Should().Contain("Test message");
        output.ToString().Should().Contain("INF");
    }

    [Fact]
    public void Debug_WithInfoLevel_DoesNotWriteToOutput()
    {
        var output = new StringWriter();
        var errorOutput = new StringWriter();
        var logger = new ConsoleLogger(AppLogLevel.Info, null, output, errorOutput);

        logger.Debug("Debug message");

        output.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Debug_WithDebugLevel_WritesToOutput()
    {
        var output = new StringWriter();
        var errorOutput = new StringWriter();
        var logger = new ConsoleLogger(AppLogLevel.Debug, null, output, errorOutput);

        logger.Debug("Debug message");

        output.ToString().Should().Contain("Debug message");
        output.ToString().Should().Contain("DBG");
    }

    [Fact]
    public void Warning_WritesToOutput()
    {
        var output = new StringWriter();
        var errorOutput = new StringWriter();
        var logger = new ConsoleLogger(AppLogLevel.Info, null, output, errorOutput);

        logger.Warning("Warning message");

        output.ToString().Should().Contain("Warning message");
        output.ToString().Should().Contain("WRN");
    }

    [Fact]
    public void Error_WritesToErrorOutput()
    {
        var output = new StringWriter();
        var errorOutput = new StringWriter();
        var logger = new ConsoleLogger(AppLogLevel.Info, null, output, errorOutput);

        logger.Error("Error message");

        errorOutput.ToString().Should().Contain("Error message");
        errorOutput.ToString().Should().Contain("ERR");
    }

    [Fact]
    public void Error_WithException_IncludesExceptionInfo()
    {
        var output = new StringWriter();
        var errorOutput = new StringWriter();
        var logger = new ConsoleLogger(AppLogLevel.Info, null, output, errorOutput);
        var exception = new InvalidOperationException("Test exception");

        logger.Error(exception, "An error occurred");

        errorOutput.ToString().Should().Contain("An error occurred");
        errorOutput.ToString().Should().Contain("InvalidOperationException");
        errorOutput.ToString().Should().Contain("Test exception");
    }

    [Fact]
    public void Info_WithFormatArgs_FormatsMessage()
    {
        var output = new StringWriter();
        var errorOutput = new StringWriter();
        var logger = new ConsoleLogger(AppLogLevel.Info, null, output, errorOutput);

        logger.Info("Processing {0} of {1} items", 5, 10);

        output.ToString().Should().Contain("Processing 5 of 10 items");
    }

    [Fact]
    public void ForContext_CreatesContextualLogger()
    {
        var output = new StringWriter();
        var errorOutput = new StringWriter();
        var logger = new ConsoleLogger(AppLogLevel.Info, null, output, errorOutput);

        var contextualLogger = logger.ForContext("SyncEngine");
        contextualLogger.Info("Starting sync");

        output.ToString().Should().Contain("[SyncEngine]");
        output.ToString().Should().Contain("Starting sync");
    }

    [Fact]
    public void ForContext_ChainedContexts_CreatesNestedContext()
    {
        var output = new StringWriter();
        var errorOutput = new StringWriter();
        var logger = new ConsoleLogger(AppLogLevel.Info, null, output, errorOutput);

        var contextualLogger = logger.ForContext("Sync").ForContext("Download");
        contextualLogger.Info("Downloading message");

        output.ToString().Should().Contain("[Sync.Download]");
    }

    [Fact]
    public void MinimumLevel_CanBeChanged()
    {
        var output = new StringWriter();
        var errorOutput = new StringWriter();
        var logger = new ConsoleLogger(AppLogLevel.Info, null, output, errorOutput);

        logger.Debug("Debug 1");
        logger.MinimumLevel = AppLogLevel.Debug;
        logger.Debug("Debug 2");

        output.ToString().Should().NotContain("Debug 1");
        output.ToString().Should().Contain("Debug 2");
    }

    [Fact]
    public void Output_IncludesTimestamp()
    {
        var output = new StringWriter();
        var errorOutput = new StringWriter();
        var logger = new ConsoleLogger(AppLogLevel.Info, null, output, errorOutput);

        logger.Info("Test");

        // Timestamp format is HH:mm:ss
        output.ToString().Should().MatchRegex(@"\d{2}:\d{2}:\d{2}");
    }

    [Theory]
    [InlineData(AppLogLevel.Debug)]
    [InlineData(AppLogLevel.Info)]
    [InlineData(AppLogLevel.Warning)]
    [InlineData(AppLogLevel.Error)]
    public void AllLogLevels_WorkCorrectly(AppLogLevel level)
    {
        var output = new StringWriter();
        var errorOutput = new StringWriter();
        var logger = new ConsoleLogger(AppLogLevel.Debug, null, output, errorOutput);

        switch (level)
        {
            case AppLogLevel.Debug:
                logger.Debug("Message");
                break;
            case AppLogLevel.Info:
                logger.Info("Message");
                break;
            case AppLogLevel.Warning:
                logger.Warning("Message");
                break;
            case AppLogLevel.Error:
                logger.Error("Message");
                break;
        }

        var combinedOutput = output.ToString() + errorOutput.ToString();
        combinedOutput.Should().Contain("Message");
    }
}

public class LoggerFactoryTests : IDisposable
{
    public LoggerFactoryTests()
    {
        LoggerFactory.Reset();
    }

    public void Dispose()
    {
        LoggerFactory.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Default_ReturnsLoggerInstance()
    {
        var logger = LoggerFactory.Default;

        logger.Should().NotBeNull();
        logger.Should().BeAssignableTo<IAppLogger>();
    }

    [Fact]
    public void Default_ReturnsSameInstance()
    {
        var logger1 = LoggerFactory.Default;
        var logger2 = LoggerFactory.Default;

        logger1.Should().BeSameAs(logger2);
    }

    [Fact]
    public void CreateConsoleLogger_ReturnsNewInstance()
    {
        var logger1 = LoggerFactory.CreateConsoleLogger();
        var logger2 = LoggerFactory.CreateConsoleLogger();

        logger1.Should().NotBeSameAs(logger2);
    }

    [Fact]
    public void CreateLogger_WithContextName_ReturnsContextualLogger()
    {
        var logger = LoggerFactory.CreateLogger("TestContext");

        logger.Should().NotBeNull();
    }

    [Fact]
    public void CreateLogger_Generic_ReturnsLoggerWithTypeName()
    {
        var logger = LoggerFactory.CreateLogger<LoggerFactoryTests>();

        logger.Should().NotBeNull();
    }

    [Fact]
    public void Configure_WithVerbose_SetsDebugLevel()
    {
        LoggerFactory.Configure(verbose: true);

        LoggerFactory.DefaultLevel.Should().Be(AppLogLevel.Debug);
    }

    [Fact]
    public void Configure_WithMinimumLevel_SetsLevel()
    {
        LoggerFactory.Configure(minimumLevel: AppLogLevel.Warning);

        LoggerFactory.DefaultLevel.Should().Be(AppLogLevel.Warning);
    }

    [Fact]
    public void DefaultLevel_CanBeSetDirectly()
    {
        LoggerFactory.DefaultLevel = AppLogLevel.Error;

        LoggerFactory.DefaultLevel.Should().Be(AppLogLevel.Error);
    }

    [Fact]
    public void Reset_RestoresDefaultSettings()
    {
        LoggerFactory.DefaultLevel = AppLogLevel.Debug;

        LoggerFactory.Reset();

        LoggerFactory.DefaultLevel.Should().Be(AppLogLevel.Info);
    }
}
