using M365MailMirror.Cli.Commands;

namespace M365MailMirror.UnitTests;

public class ProjectStructureTests
{
    [Fact]
    public void SolutionStructure_HasRequiredProjects()
    {
        // This test verifies that the solution structure is correct by checking
        // that we can reference types from all required projects

        // CLI commands exist
        var syncCommandType = typeof(SyncCommand);
        var transformCommandType = typeof(TransformCommand);
        var statusCommandType = typeof(StatusCommand);
        var verifyCommandType = typeof(VerifyCommand);
        var authLoginCommandType = typeof(AuthLoginCommand);
        var authLogoutCommandType = typeof(AuthLogoutCommand);
        var authStatusCommandType = typeof(AuthStatusCommand);

        // Verify types are loaded
        syncCommandType.Should().NotBeNull();
        transformCommandType.Should().NotBeNull();
        statusCommandType.Should().NotBeNull();
        verifyCommandType.Should().NotBeNull();
        authLoginCommandType.Should().NotBeNull();
        authLogoutCommandType.Should().NotBeNull();
        authStatusCommandType.Should().NotBeNull();
    }

    [Theory]
    [InlineData(typeof(SyncCommand), "sync")]
    [InlineData(typeof(TransformCommand), "transform")]
    [InlineData(typeof(StatusCommand), "status")]
    [InlineData(typeof(VerifyCommand), "verify")]
    [InlineData(typeof(AuthLoginCommand), "auth login")]
    [InlineData(typeof(AuthLogoutCommand), "auth logout")]
    [InlineData(typeof(AuthStatusCommand), "auth status")]
    public void Commands_HaveCorrectCommandAttribute(Type commandType, string expectedCommandName)
    {
        // Verify each command has the correct CliFx Command attribute
        var attribute = commandType.GetCustomAttributes(typeof(CliFx.Attributes.CommandAttribute), false)
            .Cast<CliFx.Attributes.CommandAttribute>()
            .FirstOrDefault();

        attribute.Should().NotBeNull();
        attribute!.Name.Should().Be(expectedCommandName);
    }
}
