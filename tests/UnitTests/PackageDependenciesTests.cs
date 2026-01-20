using MimeKit;
using Microsoft.Data.Sqlite;
using YamlDotNet.Serialization;

namespace M365MailMirror.UnitTests;

/// <summary>
/// Tests that verify all required NuGet packages are properly installed and accessible.
/// </summary>
public class PackageDependenciesTests
{
    [Fact]
    public void MimeKit_CanCreateMimeMessage()
    {
        // Verify MimeKit is available for MIME parsing
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Test Sender", "sender@example.com"));
        message.To.Add(new MailboxAddress("Test Recipient", "recipient@example.com"));
        message.Subject = "Test Subject";
        message.Body = new TextPart("plain") { Text = "Test body content" };

        message.Subject.Should().Be("Test Subject");
        message.From.Mailboxes.First().Address.Should().Be("sender@example.com");
    }

    [Fact]
    public void MicrosoftDataSqlite_CanCreateInMemoryDatabase()
    {
        // Verify Microsoft.Data.Sqlite is available for SQLite operations
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT)";
        command.ExecuteNonQuery();

        command.CommandText = "INSERT INTO test (name) VALUES ('test_value')";
        command.ExecuteNonQuery();

        command.CommandText = "SELECT name FROM test WHERE id = 1";
        var result = command.ExecuteScalar()?.ToString();

        result.Should().Be("test_value");
    }

    [Fact]
    public void YamlDotNet_CanSerializeAndDeserialize()
    {
        // Verify YamlDotNet is available for configuration parsing
        var serializer = new SerializerBuilder().Build();
        var deserializer = new DeserializerBuilder().Build();

        var testConfig = new TestConfig { Name = "test", Value = 42 };
        var yaml = serializer.Serialize(testConfig);

        var deserialized = deserializer.Deserialize<TestConfig>(yaml);

        deserialized.Name.Should().Be("test");
        deserialized.Value.Should().Be(42);
    }

    [Fact]
    public void MicrosoftGraph_TypesAreAccessible()
    {
        // Verify Microsoft.Graph types are accessible
        // Note: We don't create actual Graph client as it requires authentication
        var messageType = typeof(Microsoft.Graph.Models.Message);
        var graphClientType = typeof(Microsoft.Graph.GraphServiceClient);

        messageType.Should().NotBeNull();
        graphClientType.Should().NotBeNull();
    }

    [Fact]
    public void MicrosoftIdentityClient_TypesAreAccessible()
    {
        // Verify MSAL types are accessible
        var publicClientAppType = typeof(Microsoft.Identity.Client.PublicClientApplicationBuilder);
        var authResultType = typeof(Microsoft.Identity.Client.AuthenticationResult);

        publicClientAppType.Should().NotBeNull();
        authResultType.Should().NotBeNull();
    }

    [Fact]
    public void Bogus_CanGenerateTestData()
    {
        // Verify Bogus is available for test data generation
        var faker = new Bogus.Faker();

        var email = faker.Internet.Email();
        var subject = faker.Lorem.Sentence();
        var name = faker.Name.FullName();

        email.Should().Contain("@");
        subject.Should().NotBeNullOrEmpty();
        name.Should().NotBeNullOrEmpty();
    }

    private class TestConfig
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}
