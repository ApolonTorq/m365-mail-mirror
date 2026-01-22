using System.Runtime.CompilerServices;
using Xunit.Abstractions;

namespace M365MailMirror.IntegrationTests;

/// <summary>
/// Base class for integration tests that automatically tracks test results.
/// Uses IAsyncLifetime to track results after each test completes.
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected IntegrationTestFixture Fixture { get; }
    protected ITestOutputHelper Output { get; }

    private string? _currentTestName;
    private bool _testCompleted;
    private bool _testSkipped;
    private Exception? _testException;

    protected IntegrationTestBase(IntegrationTestFixture fixture, ITestOutputHelper output)
    {
        Fixture = fixture;
        Output = output;
    }

    /// <summary>
    /// Called before each test. Resets tracking state.
    /// </summary>
    public virtual Task InitializeAsync()
    {
        _testCompleted = false;
        _testSkipped = false;
        _testException = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called after each test. Records the test result.
    /// </summary>
    public virtual Task DisposeAsync()
    {
        if (_currentTestName != null)
        {
            var passed = _testCompleted && _testException == null;
            TestResultTracker.Record(
                GetType().Name,
                _currentTestName,
                passed,
                _testSkipped,
                _testException?.Message);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Call at the start of each test to set the test name for tracking.
    /// </summary>
    protected void TrackTest([CallerMemberName] string testName = "")
    {
        _currentTestName = testName;
    }

    /// <summary>
    /// Call at the end of successful tests to mark completion.
    /// </summary>
    protected void MarkCompleted()
    {
        _testCompleted = true;
    }

    /// <summary>
    /// Call when a test is skipped.
    /// </summary>
    protected void MarkSkipped()
    {
        _testSkipped = true;
        _testCompleted = true;
    }

    /// <summary>
    /// Wraps test execution with automatic result tracking.
    /// Recommended way to write tests for automatic tracking.
    /// </summary>
    protected async Task ExecuteTestAsync(Func<Task> testBody, [CallerMemberName] string testName = "")
    {
        _currentTestName = testName;
        try
        {
            await testBody();
            _testCompleted = true;
        }
        catch (Xunit.SkipException)
        {
            _testSkipped = true;
            _testCompleted = true;
            throw;
        }
        catch (Exception ex)
        {
            _testException = ex;
            throw;
        }
    }

    /// <summary>
    /// Synchronous version of ExecuteTestAsync.
    /// </summary>
    protected void ExecuteTest(Action testBody, [CallerMemberName] string testName = "")
    {
        _currentTestName = testName;
        try
        {
            testBody();
            _testCompleted = true;
        }
        catch (Xunit.SkipException)
        {
            _testSkipped = true;
            _testCompleted = true;
            throw;
        }
        catch (Exception ex)
        {
            _testException = ex;
            throw;
        }
    }

    /// <summary>
    /// Creates a test console that logs to both console and the integration test log file.
    /// </summary>
    protected TestConsoleWrapper CreateTestConsole()
    {
        return new TestConsoleWrapper(Output, forwardToConsole: true, logFileStream: Fixture.LogFileStream);
    }
}
