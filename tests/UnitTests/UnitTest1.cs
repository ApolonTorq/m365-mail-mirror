namespace M365MailMirror.UnitTests;

public class ProjectStructureTests
{
    [Fact]
    public void SolutionStructure_HasRequiredProjects()
    {
        // This test verifies that the solution structure is correct by checking
        // that we can reference types from all required projects

        // CLI commands exist
        var syncCommandType = typeof(Cli.Commands.SyncCommand);
        var transformCommandType = typeof(Cli.Commands.TransformCommand);
        var statusCommandType = typeof(Cli.Commands.StatusCommand);
        var verifyCommandType = typeof(Cli.Commands.VerifyCommand);
        var authLoginCommandType = typeof(Cli.Commands.AuthLoginCommand);
        var authLogoutCommandType = typeof(Cli.Commands.AuthLogoutCommand);
        var authStatusCommandType = typeof(Cli.Commands.AuthStatusCommand);

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
    [InlineData(typeof(Cli.Commands.SyncCommand), "sync")]
    [InlineData(typeof(Cli.Commands.TransformCommand), "transform")]
    [InlineData(typeof(Cli.Commands.StatusCommand), "status")]
    [InlineData(typeof(Cli.Commands.VerifyCommand), "verify")]
    [InlineData(typeof(Cli.Commands.AuthLoginCommand), "auth login")]
    [InlineData(typeof(Cli.Commands.AuthLogoutCommand), "auth logout")]
    [InlineData(typeof(Cli.Commands.AuthStatusCommand), "auth status")]
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
