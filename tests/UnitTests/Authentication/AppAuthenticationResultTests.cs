using M365MailMirror.Core.Authentication;

namespace M365MailMirror.UnitTests.Authentication;

public class AppAuthenticationResultTests
{
    [Fact]
    public void Success_CreatesSuccessfulResult()
    {
        var expiresOn = DateTimeOffset.UtcNow.AddHours(1);

        var result = AppAuthenticationResult.Success("token123", "user@example.com", expiresOn);

        result.IsSuccess.Should().BeTrue();
        result.AccessToken.Should().Be("token123");
        result.Account.Should().Be("user@example.com");
        result.ExpiresOn.Should().Be(expiresOn);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Failure_CreatesFailedResult()
    {
        var result = AppAuthenticationResult.Failure("Authentication failed");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Authentication failed");
        result.AccessToken.Should().BeNull();
        result.Account.Should().BeNull();
        result.ExpiresOn.Should().BeNull();
    }
}

public class AuthenticationStatusTests
{
    [Fact]
    public void AuthenticationStatus_DefaultValues_AreExpected()
    {
        var status = new AuthenticationStatus();

        status.IsAuthenticated.Should().BeFalse();
        status.Account.Should().BeNull();
        status.TenantId.Should().BeNull();
        status.HasCachedToken.Should().BeFalse();
        status.CacheLocation.Should().BeNull();
    }

    [Fact]
    public void AuthenticationStatus_CanSetAllProperties()
    {
        var status = new AuthenticationStatus
        {
            IsAuthenticated = true,
            Account = "user@example.com",
            TenantId = "tenant-id-123",
            HasCachedToken = true,
            CacheLocation = "/path/to/cache"
        };

        status.IsAuthenticated.Should().BeTrue();
        status.Account.Should().Be("user@example.com");
        status.TenantId.Should().Be("tenant-id-123");
        status.HasCachedToken.Should().BeTrue();
        status.CacheLocation.Should().Be("/path/to/cache");
    }
}

public class DeviceCodeInfoTests
{
    [Fact]
    public void DeviceCodeInfo_RequiredProperties_MustBeSet()
    {
        var expiresOn = DateTimeOffset.UtcNow.AddMinutes(15);

        var info = new DeviceCodeInfo
        {
            UserCode = "ABC123",
            VerificationUrl = "https://microsoft.com/devicelogin",
            Message = "To sign in, go to https://microsoft.com/devicelogin and enter ABC123",
            ExpiresOn = expiresOn
        };

        info.UserCode.Should().Be("ABC123");
        info.VerificationUrl.Should().Be("https://microsoft.com/devicelogin");
        info.Message.Should().Contain("ABC123");
        info.ExpiresOn.Should().Be(expiresOn);
    }
}
