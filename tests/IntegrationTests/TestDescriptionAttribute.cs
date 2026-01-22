namespace M365MailMirror.IntegrationTests;

/// <summary>
/// Provides a human-readable description for integration tests.
/// Used to generate the test summary at the end of the test run.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class TestDescriptionAttribute : Attribute
{
    public string Description { get; }

    public TestDescriptionAttribute(string description)
    {
        Description = description;
    }
}
