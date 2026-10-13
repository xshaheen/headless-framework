// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// A <see cref="RawJson"/> contract hands its consumer the producer's bytes on the first delivery and on a retry read
/// back from storage, whatever the host's JSON options say.
/// </summary>
public sealed class RawJsonConsumeTests : TestBase
{
    private const string _MessageName = "tests.raw-json";

    // Snake_case, odd spacing, and escapes: binding or re-serializing the body would change every one of them.
    private const string _ForeignJson = """{ "run_id" : "r1",  "note":"café \"q\"" }""";

    [Fact]
    public async Task should_hand_the_consumer_the_producer_bytes_on_the_first_delivery()
    {
        // given
        await using var provider = _BuildProvider();
        var serializer = provider.GetRequiredService<IMessageSerializer>();

        // when
        var received = await serializer.DeserializeAsync(_Transport(_ForeignJson), typeof(RawJson), AbortToken);
        await _InvokeAsync(provider, received);

        // then
        RawJsonTestConsumer.LastJson.Should().Be(_ForeignJson);
    }

    [Fact]
    public async Task should_hand_the_consumer_the_producer_bytes_on_a_retry_read_back_from_storage()
    {
        // given - the inbox stores the envelope as text and a retry reads its value back as a JsonElement
        await using var provider = _BuildProvider();
        var serializer = provider.GetRequiredService<IMessageSerializer>();
        var received = await serializer.DeserializeAsync(_Transport(_ForeignJson), typeof(RawJson), AbortToken);
        var stored = serializer.Deserialize(serializer.Serialize(received));

        // when
        await _InvokeAsync(provider, stored!);

        // then
        serializer.IsJsonType(stored!.Value!).Should().BeTrue();
        RawJsonTestConsumer.LastJson.Should().Be(_ForeignJson);
    }

    [Fact]
    public async Task should_fail_deserialization_when_the_body_is_not_json()
    {
        // given
        await using var provider = _BuildProvider();
        var serializer = provider.GetRequiredService<IMessageSerializer>();

        // when
        var act = async () => await serializer.DeserializeAsync(_Transport("run_id=r1"), typeof(RawJson), AbortToken);

        // then
        await act.Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task should_publish_the_body_unchanged()
    {
        // given
        await using var provider = _BuildProvider();
        var serializer = provider.GetRequiredService<IMessageSerializer>();
        var message = new Message(_Headers(), new RawJson(Encoding.UTF8.GetBytes(_ForeignJson)));

        // when
        var transport = await serializer.SerializeToTransportMessageAsync(message, AbortToken);

        // then
        Encoding.UTF8.GetString(transport.Body.Span).Should().Be(_ForeignJson);
    }

    private static ServiceProvider _BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureMessaging(messaging => messaging.Message<RawJson>(_MessageName));
        services.AddHeadlessMessaging(setup =>
        {
            setup.AddConsumer<RawJsonTestConsumer>();
            // Options that would rename or reject the foreign body if the serializer bound it.
            setup.Options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            setup.Options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        });

        return services.BuildServiceProvider();
    }

    private static async Task _InvokeAsync(IServiceProvider provider, Message origin)
    {
        var descriptor = provider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates().Single();
        var mediumMessage = new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = origin,
            Content = string.Empty,
            Lane = MessageLane.Bus,
            Added = DateTimeOffset.UtcNow,
        };

        await provider
            .GetRequiredService<ISubscribeInvoker>()
            .InvokeAsync(new ConsumerContext(descriptor, mediumMessage), AbortToken);
    }

    private static TransportMessage _Transport(string body) => new(_Headers(), Encoding.UTF8.GetBytes(body));

    private static Dictionary<string, string?> _Headers() =>
        new(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = _MessageName,
        };
}

[BusConsumer("tests.raw-json.consumer")]
public sealed class RawJsonTestConsumer : IConsume<RawJson>
{
    public static string? LastJson { get; private set; }

    public ValueTask ConsumeAsync(ConsumeContext<RawJson> context, CancellationToken cancellationToken)
    {
        LastJson = context.Message.ToString();
        return ValueTask.CompletedTask;
    }
}
