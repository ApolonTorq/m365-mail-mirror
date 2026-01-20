using System.Net;
using Microsoft.Graph.Models.ODataErrors;
using M365MailMirror.Core.Logging;

namespace M365MailMirror.Infrastructure.Graph;

/// <summary>
/// Provides retry logic for Graph API operations with exponential backoff
/// and proper handling of rate limiting (429) responses.
/// </summary>
public class RetryHandler
{
    private readonly IAppLogger _logger;
    private readonly RetryOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryHandler"/> class.
    /// </summary>
    public RetryHandler(IAppLogger? logger = null, RetryOptions? options = null)
    {
        _logger = logger ?? LoggerFactory.CreateLogger<RetryHandler>();
        _options = options ?? new RetryOptions();
    }

    /// <summary>
    /// Executes an async operation with retry logic.
    /// </summary>
    /// <typeparam name="T">The return type of the operation.</typeparam>
    /// <param name="operation">The async operation to execute.</param>
    /// <param name="operationName">Name of the operation for logging.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        var lastException = default(Exception);

        while (attempt < _options.MaxRetries)
        {
            attempt++;
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation();
            }
            catch (ODataError ex) when (IsRetryable(ex))
            {
                lastException = ex;
                var delay = CalculateDelay(attempt, ex);

                _logger.Warning(
                    "{0} failed (attempt {1}/{2}): {3}. Retrying in {4}ms...",
                    operationName, attempt, _options.MaxRetries, ex.Error?.Message ?? ex.Message, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
            catch (HttpRequestException ex) when (IsTransientNetworkError(ex))
            {
                lastException = ex;
                var delay = CalculateDelay(attempt, null);

                _logger.Warning(
                    "{0} failed (attempt {1}/{2}): {3}. Retrying in {4}ms...",
                    operationName, attempt, _options.MaxRetries, ex.Message, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout - retry
                lastException = new TimeoutException($"Operation {operationName} timed out");
                var delay = CalculateDelay(attempt, null);

                _logger.Warning(
                    "{0} timed out (attempt {1}/{2}). Retrying in {3}ms...",
                    operationName, attempt, _options.MaxRetries, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
        }

        _logger.Error(lastException!, "Operation {0} failed after {1} attempts", operationName, _options.MaxRetries);
        throw new GraphRetryExhaustedException($"Operation {operationName} failed after {_options.MaxRetries} attempts", lastException!);
    }

    /// <summary>
    /// Executes an async operation with retry logic (void return).
    /// </summary>
    public async Task ExecuteWithRetryAsync(
        Func<Task> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            await operation();
            return true;
        }, operationName, cancellationToken);
    }

    private static bool IsRetryable(ODataError error)
    {
        return error.ResponseStatusCode switch
        {
            // Rate limited
            429 => true,
            // Service unavailable
            503 => true,
            // Gateway timeout
            504 => true,
            // Internal server error (sometimes transient)
            500 => true,
            // Bad gateway
            502 => true,
            _ => false
        };
    }

    private static bool IsTransientNetworkError(HttpRequestException ex)
    {
        // Retry on network-level errors
        return ex.StatusCode switch
        {
            HttpStatusCode.RequestTimeout => true,
            HttpStatusCode.ServiceUnavailable => true,
            HttpStatusCode.GatewayTimeout => true,
            HttpStatusCode.BadGateway => true,
            null => true, // Network-level error with no HTTP status
            _ => false
        };
    }

    private TimeSpan CalculateDelay(int attempt, ODataError? error)
    {
        // Check for Retry-After header
        if (error?.ResponseStatusCode == 429)
        {
            // Graph API typically returns Retry-After in seconds
            // Try to extract it from the error response
            var retryAfterSeconds = ExtractRetryAfter(error);
            if (retryAfterSeconds.HasValue)
            {
                return TimeSpan.FromSeconds(Math.Min(retryAfterSeconds.Value, _options.MaxDelaySeconds));
            }
        }

        // Exponential backoff with jitter
        var baseDelay = _options.InitialDelayMs * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.NextDouble() * _options.JitterFactor * baseDelay;
        var delay = Math.Min(baseDelay + jitter, _options.MaxDelaySeconds * 1000);

        return TimeSpan.FromMilliseconds(delay);
    }

    private static int? ExtractRetryAfter(ODataError error)
    {
        // Try to get Retry-After from response headers via AdditionalData
        if (error.AdditionalData?.TryGetValue("Retry-After", out var retryAfterValue) == true)
        {
            if (int.TryParse(retryAfterValue?.ToString(), out var seconds))
            {
                return seconds;
            }
        }

        // Default retry for 429: 30 seconds as recommended by Graph API
        return 30;
    }
}

/// <summary>
/// Options for retry behavior.
/// </summary>
public class RetryOptions
{
    /// <summary>
    /// Maximum number of retry attempts. Default is 5.
    /// </summary>
    public int MaxRetries { get; init; } = 5;

    /// <summary>
    /// Initial delay in milliseconds before the first retry. Default is 1000ms.
    /// </summary>
    public int InitialDelayMs { get; init; } = 1000;

    /// <summary>
    /// Maximum delay in seconds between retries. Default is 120 seconds.
    /// </summary>
    public int MaxDelaySeconds { get; init; } = 120;

    /// <summary>
    /// Jitter factor (0.0 to 1.0) to randomize delay. Default is 0.2.
    /// </summary>
    public double JitterFactor { get; init; } = 0.2;
}

/// <summary>
/// Exception thrown when all retry attempts have been exhausted.
/// </summary>
public class GraphRetryExhaustedException : Exception
{
    public GraphRetryExhaustedException(string message, Exception innerException)
        : base(message, innerException) { }
}
