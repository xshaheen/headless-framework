using System.Reflection;
using Framework.Abstractions;
using Framework.Core;
using Framework.Messages;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public class SubscribeInvokerWithCancellation
{
    private readonly IServiceProvider _serviceProvider;

    public SubscribeInvokerWithCancellation()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton<IBootstrapper, Bootstrapper>();
        serviceCollection.AddSingleton<ISerializer, JsonUtf8Serializer>();
        serviceCollection.AddSingleton<ISubscribeInvoker, SubscribeInvoker>();
        serviceCollection.AddSingleton<ILongIdGenerator>(_ => new SnowflakeIdLongIdGenerator());
        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    private ISubscribeInvoker SubscribeInvoker => _serviceProvider.GetService<ISubscribeInvoker>()!;

    [Fact]
    public async Task InvokeTest()
    {
        var longIdGenerator = _serviceProvider.GetRequiredService<ILongIdGenerator>();
        var descriptor = new ConsumerExecutorDescriptor
        {
            Attribute = new CandidatesTopic("fake.output.withcancellation"),
            ServiceTypeInfo = typeof(FakeSubscriberWithCancellation).GetTypeInfo(),
            ImplTypeInfo = typeof(FakeSubscriberWithCancellation).GetTypeInfo(),
            MethodInfo = typeof(FakeSubscriberWithCancellation).GetMethod(
                nameof(FakeSubscriberWithCancellation.CancellationTokenInjected),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                null,
                new[] { typeof(CancellationToken) },
                null
            )!,
            Parameters = new List<ParameterDescriptor>
            {
                new()
                {
                    ParameterType = typeof(CancellationToken),
                    IsFromCap = true,
                    Name = "cancellationToken",
                },
            },
        };

        var header = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = longIdGenerator.Create().ToString(CultureInfo.InvariantCulture),
            [Headers.MessageName] = "fake.output.withcancellation",
        };
        var message = new Message(header, null);
        var mediumMessage = new MediumMessage
        {
            DbId = "12",
            Origin = message,
            Content = null!,
        };
        var context = new ConsumerContext(descriptor, mediumMessage);

        var cancellationToken = CancellationToken.None;
        var ret = await SubscribeInvoker.InvokeAsync(context, cancellationToken);
        ret.Result.Should().Be(cancellationToken);
    }
}

public class FakeSubscriberWithCancellation : IConsumer
{
    [CapSubscribe("fake.output.withcancellation")]
    public CancellationToken CancellationTokenInjected(CancellationToken cancellationToken)
    {
        return cancellationToken;
    }
}
