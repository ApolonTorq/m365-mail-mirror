namespace M365MailMirror.IntegrationTests;

/// <summary>
/// Collection definition for integration tests.
/// Tests in the same collection run sequentially and share a single fixture instance.
/// This prevents AAD throttling by reusing authentication across all tests.
/// </summary>
[CollectionDefinition("IntegrationTests")]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
    // This class is never instantiated, used only for xUnit collection attribute.
    // All test classes marked with [Collection("IntegrationTests")] will:
    // 1. Run sequentially to avoid conflicts with shared resources
    // 2. Share a single IntegrationTestFixture instance (injected via constructor)
}
