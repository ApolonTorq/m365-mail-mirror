using System.Collections.Concurrent;

namespace M365MailMirror.IntegrationTests;

/// <summary>
/// Tracks test results for reporting in the integration test summary.
/// Thread-safe static tracker that tests register with upon completion.
/// </summary>
public static class TestResultTracker
{
    private static readonly ConcurrentDictionary<string, TestResult> Results = new();

    /// <summary>
    /// Records a test result.
    /// </summary>
    /// <param name="className">The test class name.</param>
    /// <param name="methodName">The test method name.</param>
    /// <param name="passed">Whether the test passed.</param>
    /// <param name="skipped">Whether the test was skipped.</param>
    /// <param name="errorMessage">Optional error message if the test failed.</param>
    public static void Record(string className, string methodName, bool passed, bool skipped = false, string? errorMessage = null)
    {
        var key = $"{className}.{methodName}";
        Results[key] = new TestResult(className, methodName, passed, skipped, errorMessage);
    }

    /// <summary>
    /// Gets the result for a specific test, or null if not recorded.
    /// </summary>
    public static TestResult? GetResult(string className, string methodName)
    {
        var key = $"{className}.{methodName}";
        return Results.TryGetValue(key, out var result) ? result : null;
    }

    /// <summary>
    /// Gets all recorded results.
    /// </summary>
    public static IReadOnlyCollection<TestResult> GetAllResults() => Results.Values.ToList();

    /// <summary>
    /// Clears all recorded results. Called at the start of a test run.
    /// </summary>
    public static void Clear() => Results.Clear();

    /// <summary>
    /// Gets summary counts.
    /// </summary>
    public static (int Total, int Passed, int Failed, int Skipped) GetSummary()
    {
        var results = Results.Values.ToList();
        return (
            results.Count,
            results.Count(r => r.Passed && !r.Skipped),
            results.Count(r => !r.Passed && !r.Skipped),
            results.Count(r => r.Skipped)
        );
    }
}

/// <summary>
/// Represents the result of a single test.
/// </summary>
public record TestResult(
    string ClassName,
    string MethodName,
    bool Passed,
    bool Skipped,
    string? ErrorMessage = null);
