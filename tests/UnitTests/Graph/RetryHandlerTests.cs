using M365MailMirror.Infrastructure.Graph;

namespace M365MailMirror.UnitTests.Graph;

public class RetryHandlerTests
{
    [Fact]
    public async Task ExecuteWithRetryAsync_SucceedsOnFirstAttempt_ReturnsResult()
    {
        var handler = new RetryHandler();
        var callCount = 0;

        var result = await handler.ExecuteWithRetryAsync(async () =>
        {
            callCount++;
            await Task.CompletedTask;
            return 42;
        }, "TestOperation");

        result.Should().Be(42);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_FailsThenSucceeds_RetriesAndReturnsResult()
    {
        var handler = new RetryHandler(options: new RetryOptions { InitialDelayMs = 10 });
        var callCount = 0;

        var result = await handler.ExecuteWithRetryAsync(async () =>
        {
            callCount++;
            if (callCount < 3)
            {
                await Task.CompletedTask;
                throw new HttpRequestException("Network error", null, System.Net.HttpStatusCode.ServiceUnavailable);
            }

            return "success";
        }, "TestOperation");

        result.Should().Be("success");
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_ExhaustsRetries_ThrowsRetryExhaustedException()
    {
        var handler = new RetryHandler(options: new RetryOptions { MaxRetries = 3, InitialDelayMs = 10 });
        var callCount = 0;

        var act = async () => await handler.ExecuteWithRetryAsync(async () =>
        {
            callCount++;
            await Task.CompletedTask;
            throw new HttpRequestException("Network error", null, System.Net.HttpStatusCode.ServiceUnavailable);
        }, "TestOperation");

        await act.Should().ThrowAsync<GraphRetryExhaustedException>()
            .WithMessage("*failed after 3 attempts*");

        callCount.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_NonRetryableError_ThrowsImmediately()
    {
        var handler = new RetryHandler();
        var callCount = 0;

        var act = async () => await handler.ExecuteWithRetryAsync<int>(async () =>
        {
            callCount++;
            await Task.CompletedTask;
            throw new InvalidOperationException("Not retryable");
        }, "TestOperation");

        await act.Should().ThrowAsync<InvalidOperationException>();
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var handler = new RetryHandler();
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await handler.ExecuteWithRetryAsync(async () =>
        {
            await Task.CompletedTask;
            return 42;
        }, "TestOperation", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_VoidOperation_SucceedsOnFirstAttempt()
    {
        var handler = new RetryHandler();
        var callCount = 0;

        await handler.ExecuteWithRetryAsync(async () =>
        {
            callCount++;
            await Task.CompletedTask;
        }, "TestOperation");

        callCount.Should().Be(1);
    }
}

public class RetryOptionsTests
{
    [Fact]
    public void RetryOptions_DefaultValues_AreExpected()
    {
        var options = new RetryOptions();

        options.MaxRetries.Should().Be(5);
        options.InitialDelayMs.Should().Be(1000);
        options.MaxDelaySeconds.Should().Be(120);
        options.JitterFactor.Should().Be(0.2);
    }

    [Fact]
    public void RetryOptions_CanBeCustomized()
    {
        var options = new RetryOptions
        {
            MaxRetries = 10,
            InitialDelayMs = 500,
            MaxDelaySeconds = 60,
            JitterFactor = 0.5
        };

        options.MaxRetries.Should().Be(10);
        options.InitialDelayMs.Should().Be(500);
        options.MaxDelaySeconds.Should().Be(60);
        options.JitterFactor.Should().Be(0.5);
    }
}
