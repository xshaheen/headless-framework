using System;
using System.Reflection;
using Framework.Abstractions;
using Framework.Core;
using Framework.Messages;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Serialization;
using Framework.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SubscribeInvokerTest : TestBase
{
    private readonly IServiceProvider _serviceProvider;

    public SubscribeInvokerTest()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton<ISerializer, JsonUtf8Serializer>();
        serviceCollection.AddSingleton<ISubscribeInvoker, SubscribeInvoker>();
        serviceCollection.AddSingleton<ILongIdGenerator>(_ => new SnowflakeIdLongIdGenerator());
        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    private ISubscribeInvoker SubscribeInvoker => _serviceProvider.GetService<ISubscribeInvoker>()!;

    [Fact]
    public async Task InvokeTest()
    {
        var snowflakeId = _serviceProvider.GetRequiredService<ILongIdGenerator>();
        var descriptor = new ConsumerExecutorDescriptor
        {
            Attribute = new CandidatesTopic("fake.output.integer"),
            ServiceTypeInfo = typeof(FakeSubscriber).GetTypeInfo(),
            ImplTypeInfo = typeof(FakeSubscriber).GetTypeInfo(),
            MethodInfo = typeof(FakeSubscriber).GetMethod(
                nameof(FakeSubscriber.OutputIntegerSub),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null
            )!,
            Parameters = new List<ParameterDescriptor>(),
        };

        var header = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = snowflakeId.Create().ToString(CultureInfo.InvariantCulture),
            [Headers.MessageName] = "fake.output.integer",
        };
        var message = new Message(header, null);
        var mediumMessage = new MediumMessage
        {
            DbId = "123",
            Origin = message,
            Content = JsonSerializer.Serialize(message),
        };
        var context = new ConsumerContext(descriptor, mediumMessage);

        var ret = await SubscribeInvoker.InvokeAsync(context, AbortToken);
        ret.Result.Should().Be(int.MaxValue);
    }
}

public sealed class FakeSubscriber : IConsumer
{
    [CapSubscribe("fake.output.integer")]
    public int OutputIntegerSub() => int.MaxValue;
}
