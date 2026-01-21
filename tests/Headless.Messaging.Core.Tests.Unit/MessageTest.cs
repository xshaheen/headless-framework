using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;
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
        // given
        var givenMessage = new Message(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { "headless-msg-name", "authentication.users.update" },
                { "headless-msg-type", "User" },
                { "headless-corr-seq", "0" },
                { "headless-msg-group", "service.v1" },
            },
            value: new MessageValue("test@test.com", "User")
        );

        // when
        var serializer = _provider.GetRequiredService<ISerializer>();
        var json = serializer.Serialize(givenMessage);
        var deserializedMessage = serializer.Deserialize(json);

        // then
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
