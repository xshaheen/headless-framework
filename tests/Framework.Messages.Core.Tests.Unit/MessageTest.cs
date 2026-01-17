using Framework.Messages.Messages;
using Framework.Messages.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public class MessageTest
{
    private readonly IServiceProvider _provider;

    public MessageTest()
    {
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddSingleton<IServiceCollection>(_ => services);
        services.AddSingleton<ISerializer, JsonUtf8Serializer>();
        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public void Serialize_then_Deserialize_Message_With_Utf8JsonSerializer()
    {
        // Given
        var givenMessage = new Message(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { "cap-msg-name", "authentication.users.update" },
                { "cap-msg-type", "User" },
                { "cap-corr-seq", "0" },
                { "cap-msg-group", "service.v1" },
            },
            value: new MessageValue("test@test.com", "User")
        );

        // When
        var serializer = _provider.GetRequiredService<ISerializer>();
        var json = serializer.Serialize(givenMessage);
        var deserializedMessage = serializer.Deserialize(json);

        // Then
        serializer.IsJsonType(deserializedMessage?.Value).Should().BeTrue();

        var result = serializer.Deserialize(deserializedMessage.Value!, typeof(MessageValue)) as MessageValue;
        result.Should().NotBeNull();
        ((MessageValue)givenMessage.Value!).Email.Should().Be(result.Email);
        ((MessageValue)givenMessage.Value!).Name.Should().Be(result.Name);
    }
}

public class MessageValue(string email, string name)
{
    public string Email { get; } = email;

    public string Name { get; } = name;
}
