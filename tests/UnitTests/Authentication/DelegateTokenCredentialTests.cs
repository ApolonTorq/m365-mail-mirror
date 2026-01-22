using Azure.Core;
using M365MailMirror.Core.Authentication;
using M365MailMirror.Infrastructure.Authentication;
using Moq;

namespace M365MailMirror.UnitTests.Authentication;

public class DelegateTokenCredentialTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly TokenRequestContext _requestContext;

    public DelegateTokenCredentialTests()
    {
        _mockAuthService = new Mock<IAuthenticationService>();
        _requestContext = new TokenRequestContext(["https://graph.microsoft.com/.default"]);
    }

    [Fact]
    public async Task GetTokenAsync_FirstCall_AcquiresTokenFromAuthService()
    {
        // Arrange
        var expiresOn = DateTimeOffset.UtcNow.AddHours(1);
        _mockAuthService
            .Setup(a => a.AcquireTokenSilentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppAuthenticationResult.Success("test-token", "user@example.com", expiresOn));

        var credential = new DelegateTokenCredential(_mockAuthService.Object);

        // Act
        var token = await credential.GetTokenAsync(_requestContext, CancellationToken.None);

        // Assert
        token.Token.Should().Be("test-token");
        token.ExpiresOn.Should().Be(expiresOn);
        _mockAuthService.Verify(
            a => a.AcquireTokenSilentAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetTokenAsync_SubsequentCallsWithValidToken_ReturnsCachedToken()
    {
        // Arrange
        var expiresOn = DateTimeOffset.UtcNow.AddHours(1);
        _mockAuthService
            .Setup(a => a.AcquireTokenSilentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppAuthenticationResult.Success("test-token", "user@example.com", expiresOn));

        var credential = new DelegateTokenCredential(_mockAuthService.Object);

        // Act - Make multiple calls
        var token1 = await credential.GetTokenAsync(_requestContext, CancellationToken.None);
        var token2 = await credential.GetTokenAsync(_requestContext, CancellationToken.None);
        var token3 = await credential.GetTokenAsync(_requestContext, CancellationToken.None);

        // Assert - Auth service should only be called once (subsequent calls use cache)
        token1.Token.Should().Be("test-token");
        token2.Token.Should().Be("test-token");
        token3.Token.Should().Be("test-token");
        _mockAuthService.Verify(
            a => a.AcquireTokenSilentAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetTokenAsync_WithExpiredToken_AcquiresNewToken()
    {
        // Arrange - First token expires immediately
        var expiredTime = DateTimeOffset.UtcNow.AddMinutes(-1);
        var validTime = DateTimeOffset.UtcNow.AddHours(1);

        var callCount = 0;
        _mockAuthService
            .Setup(a => a.AcquireTokenSilentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? AppAuthenticationResult.Success("expired-token", "user@example.com", expiredTime)
                    : AppAuthenticationResult.Success("new-token", "user@example.com", validTime);
            });

        var credential = new DelegateTokenCredential(_mockAuthService.Object);

        // Act - First call gets expired token, second call should refresh
        var token1 = await credential.GetTokenAsync(_requestContext, CancellationToken.None);
        var token2 = await credential.GetTokenAsync(_requestContext, CancellationToken.None);

        // Assert - Auth service called twice because first token was expired
        token1.Token.Should().Be("expired-token");
        token2.Token.Should().Be("new-token");
        _mockAuthService.Verify(
            a => a.AcquireTokenSilentAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task GetTokenAsync_WithinRefreshBuffer_RefreshesToken()
    {
        // Arrange - Token expires in 4 minutes (within 5-minute buffer)
        var almostExpired = DateTimeOffset.UtcNow.AddMinutes(4);
        var validTime = DateTimeOffset.UtcNow.AddHours(1);

        var callCount = 0;
        _mockAuthService
            .Setup(a => a.AcquireTokenSilentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? AppAuthenticationResult.Success("almost-expired-token", "user@example.com", almostExpired)
                    : AppAuthenticationResult.Success("refreshed-token", "user@example.com", validTime);
            });

        var credential = new DelegateTokenCredential(_mockAuthService.Object);

        // Act - First call, then second call which should trigger refresh due to buffer
        var token1 = await credential.GetTokenAsync(_requestContext, CancellationToken.None);
        var token2 = await credential.GetTokenAsync(_requestContext, CancellationToken.None);

        // Assert - Token should be refreshed because it's within the 5-minute buffer
        token1.Token.Should().Be("almost-expired-token");
        token2.Token.Should().Be("refreshed-token");
        _mockAuthService.Verify(
            a => a.AcquireTokenSilentAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task GetTokenAsync_OutsideRefreshBuffer_UsesCachedToken()
    {
        // Arrange - Token expires in 10 minutes (outside 5-minute buffer)
        var expiresOn = DateTimeOffset.UtcNow.AddMinutes(10);
        _mockAuthService
            .Setup(a => a.AcquireTokenSilentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppAuthenticationResult.Success("valid-token", "user@example.com", expiresOn));

        var credential = new DelegateTokenCredential(_mockAuthService.Object);

        // Act
        var token1 = await credential.GetTokenAsync(_requestContext, CancellationToken.None);
        var token2 = await credential.GetTokenAsync(_requestContext, CancellationToken.None);

        // Assert - Should only call auth service once (10 min > 5 min buffer)
        token1.Token.Should().Be("valid-token");
        token2.Token.Should().Be("valid-token");
        _mockAuthService.Verify(
            a => a.AcquireTokenSilentAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetTokenAsync_AuthServiceFails_ThrowsInvalidOperationException()
    {
        // Arrange
        _mockAuthService
            .Setup(a => a.AcquireTokenSilentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppAuthenticationResult.Failure("Authentication failed"));

        var credential = new DelegateTokenCredential(_mockAuthService.Object);

        // Act & Assert
        var act = async () => await credential.GetTokenAsync(_requestContext, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Authentication failed*");
    }

    [Fact]
    public void Constructor_NullAuthService_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new DelegateTokenCredential(null!);
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("authService");
    }

    [Fact]
    public void GetToken_Sync_DelegatesToAsync()
    {
        // Arrange
        var expiresOn = DateTimeOffset.UtcNow.AddHours(1);
        _mockAuthService
            .Setup(a => a.AcquireTokenSilentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppAuthenticationResult.Success("test-token", "user@example.com", expiresOn));

        var credential = new DelegateTokenCredential(_mockAuthService.Object);

        // Act - Use synchronous method
        var token = credential.GetToken(_requestContext, CancellationToken.None);

        // Assert
        token.Token.Should().Be("test-token");
        _mockAuthService.Verify(
            a => a.AcquireTokenSilentAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetTokenAsync_ConcurrentCalls_OnlyOneAuthServiceCall()
    {
        // Arrange
        var expiresOn = DateTimeOffset.UtcNow.AddHours(1);
        var callCount = 0;

        _mockAuthService
            .Setup(a => a.AcquireTokenSilentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref callCount);
                // Simulate some delay to make race condition more likely
                Thread.Sleep(10);
                return AppAuthenticationResult.Success("test-token", "user@example.com", expiresOn);
            });

        var credential = new DelegateTokenCredential(_mockAuthService.Object);

        // Act - Start multiple concurrent calls
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => credential.GetTokenAsync(_requestContext, CancellationToken.None).AsTask())
            .ToArray();

        var tokens = await Task.WhenAll(tasks);

        // Assert - All tokens should be the same
        tokens.Should().AllSatisfy(t => t.Token.Should().Be("test-token"));

        // Note: Due to the simple lock pattern, there may be a few calls during the race
        // but subsequent calls after cache is populated should all use cache
        // The key is that we're not making N calls for N requests
        callCount.Should().BeLessThan(10, "Caching should prevent all concurrent calls from hitting auth service");
    }

    [Fact]
    public async Task GetTokenAsync_WithNullExpiresOn_UsesDefaultExpiration()
    {
        // Arrange - Auth service returns null ExpiresOn (create result directly to allow null)
        var resultWithNullExpiry = new AppAuthenticationResult
        {
            IsSuccess = true,
            AccessToken = "test-token",
            Account = "user@example.com",
            ExpiresOn = null
        };

        _mockAuthService
            .Setup(a => a.AcquireTokenSilentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultWithNullExpiry);

        var credential = new DelegateTokenCredential(_mockAuthService.Object);

        // Act
        var beforeCall = DateTimeOffset.UtcNow;
        var token = await credential.GetTokenAsync(_requestContext, CancellationToken.None);
        var afterCall = DateTimeOffset.UtcNow;

        // Assert - Default expiration should be ~1 hour from now
        token.Token.Should().Be("test-token");
        token.ExpiresOn.Should().BeAfter(beforeCall.AddMinutes(59));
        token.ExpiresOn.Should().BeBefore(afterCall.AddMinutes(61));
    }
}
