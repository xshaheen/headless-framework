// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Configuration;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Every-instance Bus subscriptions on Azure Service Bus, with the administration and messaging clients faked: one
/// subscription per process, created on subscribe with a 5-minute idle deletion, recreated when Azure deleted it, and
/// deleted only when the host disposes the factory.
/// </summary>
public sealed class AzureServiceBusEveryInstanceTests : TestBase
{
    private const string _Identity = "pricing.price-cache";
    private const string _TopicPath = "headless";

    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly ServiceBusAdministrationClient _admin = Substitute.For<ServiceBusAdministrationClient>();
    private readonly IAzureServiceBusClientPool _pool = Substitute.For<IAzureServiceBusClientPool>();
    private readonly List<string> _brokerRules = [];
    private bool _subscriptionExists;

    public AzureServiceBusEveryInstanceTests()
    {
        var serviceBusClient = Substitute.For<ServiceBusClient>();
        serviceBusClient
            .CreateProcessor(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ServiceBusProcessorOptions>())
            .Returns(_ => Substitute.For<ServiceBusProcessor>());
        _pool.GetClient().Returns(serviceBusClient);
        _pool.GetAdministrationClient().Returns(_admin);

        _admin
            .TopicExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Response.FromValue(true, Substitute.For<Response>())));
        _admin
            .SubscriptionExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Response.FromValue(_subscriptionExists, Substitute.For<Response>())));
        _admin
            .CreateSubscriptionAsync(
                Arg.Any<CreateSubscriptionOptions>(),
                Arg.Any<CreateRuleOptions>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                _subscriptionExists = true;
                _brokerRules.Clear();
                _brokerRules.Add(call.Arg<CreateRuleOptions>().Name);
                return Task.FromResult<Response<SubscriptionProperties>>(null!);
            });
        _admin
            .GetRulesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
                AsyncPageable<RuleProperties>.FromPages([
                    Page<RuleProperties>.FromValues(
                        [
                            .. _brokerRules.Select(name =>
                                ServiceBusModelFactory.RuleProperties(name, new TrueRuleFilter())
                            ),
                        ],
                        continuationToken: null,
                        Substitute.For<Response>()
                    ),
                ])
            );
    }

    [Fact]
    public void should_name_a_subscription_per_process_within_the_azure_limits()
    {
        // given
        var mine = _Request(_instanceId);
        var theirs = _Request(Guid.NewGuid());

        // when
        var name = AzureServiceBusConsumerClientFactory.BusSubscriptionName(mine);

        // then
        name.Length.Should().BeLessThanOrEqualTo(50);
        name.Should().MatchRegex("^[A-Za-z0-9][A-Za-z0-9._-]*[A-Za-z0-9]$");
        name.Should().StartWith(_Identity);
        name.Should().Be(AzureServiceBusConsumerClientFactory.BusSubscriptionName(mine with { }));
        name.Should().NotBe(AzureServiceBusConsumerClientFactory.BusSubscriptionName(theirs));
        name.Should().NotBe(AzureServiceBusConsumerClientFactory.BusSubscriptionName(_Identity));
    }

    [Fact]
    public async Task should_refuse_an_every_instance_client_without_auto_provision()
    {
        // given
        await using var factory = _CreateFactory(autoProvision: false);

        // when
        var act = async () => await factory.CreateAsync(_Request(_instanceId), AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*'{_Identity}'*AutoProvision*");
        _pool.DidNotReceive().GetClient();
        _pool.DidNotReceive().GetAdministrationClient();
    }

    [Fact]
    public async Task should_create_no_subscription_for_a_client_that_never_subscribes()
    {
        // given
        await using var factory = _CreateFactory();

        // when: the core's topology-only client only reads message names, then goes away
        var client = await factory.CreateAsync(_Request(_instanceId), AbortToken);
        await client.FetchMessageNamesAsync(["PriceChanged"], AbortToken);
        await client.DisposeAsync();

        // then
        await _admin.DidNotReceiveWithAnyArgs().SubscriptionExistsAsync(default!, default!, AbortToken);
        await _admin
            .DidNotReceive()
            .CreateSubscriptionAsync(Arg.Any<CreateSubscriptionOptions>(), Arg.Any<CancellationToken>());
        await _admin
            .DidNotReceive()
            .CreateSubscriptionAsync(
                Arg.Any<CreateSubscriptionOptions>(),
                Arg.Any<CreateRuleOptions>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_create_the_subscription_on_subscribe_with_a_five_minute_idle_deletion()
    {
        // given
        await using var factory = _CreateFactory();
        await using var client = await factory.CreateAsync(_Request(_instanceId), AbortToken);

        // when
        await client.SubscribeAsync(["PriceChanged", "RateChanged"], AbortToken);

        // then
        await _admin
            .Received(1)
            .CreateSubscriptionAsync(
                Arg.Is<CreateSubscriptionOptions>(options =>
                    options.TopicName == _TopicPath
                    && options.SubscriptionName == _SubscriptionName
                    && options.AutoDeleteOnIdle == TimeSpan.FromMinutes(5)
                ),
                Arg.Is<CreateRuleOptions>(rule => rule.Name == "PriceChanged"),
                Arg.Any<CancellationToken>()
            );
        await _admin
            .Received(1)
            .CreateRuleAsync(
                _TopicPath,
                _SubscriptionName,
                Arg.Is<CreateRuleOptions>(rule => rule.Name == "RateChanged"),
                Arg.Any<CancellationToken>()
            );
        await _admin.DidNotReceiveWithAnyArgs().DeleteRuleAsync(default!, default!, default!, AbortToken);
    }

    [Fact]
    public async Task should_resume_the_subscription_a_rebuilt_client_finds()
    {
        // given: a client the core rebuilt after a failure, with the subscription its predecessor created
        _subscriptionExists = true;
        _brokerRules.Add("PriceChanged");
        await using var factory = _CreateFactory();
        await using var client = await factory.CreateAsync(_Request(_instanceId), AbortToken);

        // when
        await client.SubscribeAsync(["PriceChanged"], AbortToken);

        // then
        await _admin
            .DidNotReceive()
            .CreateSubscriptionAsync(
                Arg.Any<CreateSubscriptionOptions>(),
                Arg.Any<CreateRuleOptions>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_delete_the_subscription_only_when_the_factory_is_disposed()
    {
        // given
        var factory = _CreateFactory();
        var client = await factory.CreateAsync(_Request(_instanceId), AbortToken);
        await client.SubscribeAsync(["PriceChanged"], AbortToken);

        // when: the core disposes its clients on every rebuild as well as on shutdown
        await client.ShutdownAsync(TimeSpan.FromSeconds(1), AbortToken);
        await client.DisposeAsync();

        // then
        await _admin.DidNotReceiveWithAnyArgs().DeleteSubscriptionAsync(default!, default!, AbortToken);

        // when: the host disposes the container after the core stopped
        await factory.DisposeAsync();

        // then
        await _admin.Received(1).DeleteSubscriptionAsync(_TopicPath, _SubscriptionName, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_fail_the_shutdown_when_a_subscription_cannot_be_deleted()
    {
        // given
        var factory = _CreateFactory();
        await using (var gone = await factory.CreateAsync(_Request(_instanceId), AbortToken))
        {
            await gone.SubscribeAsync(["PriceChanged"], AbortToken);
        }

        await using (var failing = await factory.CreateAsync(_Request(Guid.NewGuid()), AbortToken))
        {
            await failing.SubscribeAsync(["PriceChanged"], AbortToken);
        }

        _admin
            .DeleteSubscriptionAsync(_TopicPath, _SubscriptionName, Arg.Any<CancellationToken>())
            .Returns<Task<Response>>(_ =>
                throw new ServiceBusException("gone", ServiceBusFailureReason.MessagingEntityNotFound)
            );
        _admin
            .DeleteSubscriptionAsync(
                _TopicPath,
                Arg.Is<string>(name => name != _SubscriptionName),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<Response>>(_ => throw new RequestFailedException("namespace unreachable"));

        // when
        var act = async () => await factory.DisposeAsync();

        // then
        await act.Should().NotThrowAsync("Azure deletes an idle every-instance subscription on its own");
        await _admin.ReceivedWithAnyArgs(2).DeleteSubscriptionAsync(default!, default!, AbortToken);
    }

    [Fact]
    public async Task should_explain_missing_manage_rights_when_the_subscription_cannot_be_created()
    {
        // given
        _admin
            .SubscriptionExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<Response<bool>>>(_ => throw new UnauthorizedAccessException("Manage claim required"));
        await using var factory = _CreateFactory();
        await using var client = await factory.CreateAsync(_Request(_instanceId), AbortToken);

        // when
        var act = async () => await client.SubscribeAsync(["PriceChanged"], AbortToken);

        // then
        (
            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage($"*'{_SubscriptionName}'*Manage rights*")
        ).WithInnerException<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task should_recreate_a_subscription_azure_deleted_and_report_it()
    {
        // given
        await using var factory = _CreateFactory();
        await using var client = await factory.CreateAsync(_Request(_instanceId), AbortToken);
        var reestablished = 0;
        client.AttachCallbacks(onMessage: null, onLog: _ => { });
        client.AttachReestablishedCallback(_ =>
        {
            Interlocked.Increment(ref reestablished);
            return Task.CompletedTask;
        });
        await client.SubscribeAsync(["PriceChanged"], AbortToken);

        // when: Azure deleted the subscription after a long disconnect, and the processor reports the missing entity
        _subscriptionExists = false;
        _brokerRules.Clear();
        await _RaiseProcessorErrorAsync(
            client,
            new ServiceBusException("gone", ServiceBusFailureReason.MessagingEntityNotFound)
        );

        // then
        await _admin
            .Received(2)
            .CreateSubscriptionAsync(
                Arg.Is<CreateSubscriptionOptions>(options => options.SubscriptionName == _SubscriptionName),
                Arg.Is<CreateRuleOptions>(rule => rule.Name == "PriceChanged"),
                Arg.Any<CancellationToken>()
            );
        reestablished.Should().Be(1, "messages published while the subscription was gone never arrive");
    }

    [Fact]
    public async Task should_not_report_a_gap_when_the_subscription_still_exists_after_a_missing_entity_error()
    {
        // given
        await using var factory = _CreateFactory();
        await using var client = await factory.CreateAsync(_Request(_instanceId), AbortToken);
        var reestablished = 0;
        client.AttachCallbacks(onMessage: null, onLog: _ => { });
        client.AttachReestablishedCallback(_ =>
        {
            Interlocked.Increment(ref reestablished);
            return Task.CompletedTask;
        });
        await client.SubscribeAsync(["PriceChanged"], AbortToken);

        // when: the processor reports a missing entity, but the subscription is still there
        await _RaiseProcessorErrorAsync(
            client,
            new ServiceBusException("transient", ServiceBusFailureReason.MessagingEntityNotFound)
        );

        // then: nothing was recreated, so nothing was lost and the consumer keeps its state
        await _admin
            .Received(1)
            .CreateSubscriptionAsync(
                Arg.Any<CreateSubscriptionOptions>(),
                Arg.Any<CreateRuleOptions>(),
                Arg.Any<CancellationToken>()
            );
        reestablished.Should().Be(0);
    }

    [Fact]
    public async Task should_give_each_subscription_delete_its_own_timeout_when_the_factory_is_disposed()
    {
        // given: two every-instance subscriptions, the first of which hangs until its delete times out
        var time = new FakeTimeProvider();
        var factory = _CreateFactory(timeProvider: time);
        await using (var first = await factory.CreateAsync(_Request(_instanceId), AbortToken))
        {
            await first.SubscribeAsync(["PriceChanged"], AbortToken);
        }

        await using (var second = await factory.CreateAsync(_Request(Guid.NewGuid()), AbortToken))
        {
            await second.SubscribeAsync(["PriceChanged"], AbortToken);
        }

        var firstDeleteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokenCancelledAtCall = new List<bool>();
        _admin
            .DeleteSubscriptionAsync(_TopicPath, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var token = call.Arg<CancellationToken>();
                lock (tokenCancelledAtCall)
                {
                    tokenCancelledAtCall.Add(token.IsCancellationRequested);
                }

                if (firstDeleteStarted.TrySetResult())
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }

                return Substitute.For<Response>();
            });

        // when
        var disposing = factory.DisposeAsync().AsTask();
        await firstDeleteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        time.Advance(AzureServiceBusConsumerClientFactory.CleanupTimeoutPerSubscription);
        await disposing.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then: the slow first delete did not spend the second delete's budget
        tokenCancelledAtCall.Should().Equal(false, false);
    }

    [Fact]
    public async Task should_leave_a_competing_subscription_to_its_operator_when_it_is_missing()
    {
        // given
        await using var factory = _CreateFactory();
        await using var client = await factory.CreateAsync(
            new ConsumerClientRequest(_Identity, 1, MessageLane.Bus),
            AbortToken
        );
        client.AttachCallbacks(onMessage: null, onLog: _ => { });
        _admin.ClearReceivedCalls();

        // when
        await _RaiseProcessorErrorAsync(
            client,
            new ServiceBusException("gone", ServiceBusFailureReason.MessagingEntityNotFound)
        );

        // then
        await _admin.DidNotReceiveWithAnyArgs().SubscriptionExistsAsync(default!, default!, AbortToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_declare_every_instance_support_only_with_auto_provision(bool autoProvision)
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
            setup.UseAzureServiceBus(options =>
            {
                options.ConnectionString =
                    "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey";
                options.AutoProvision = autoProvision;
            })
        );
        await using var provider = services.BuildServiceProvider();

        // when
        var capabilities = provider.GetRequiredService<MessagingProviderCapabilities>();

        // then
        capabilities.SupportsEveryInstance.Should().Be(autoProvision);
    }

    [Fact]
    public async Task should_fail_startup_before_any_client_exists_when_auto_provision_is_off()
    {
        // given
        var clientsCreated = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureMessaging(messaging => messaging.AddModule<PriceCacheModule>());
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseAzureServiceBus(options =>
            {
                options.ConnectionString =
                    "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey";
                options.AutoProvision = false;
            });
            setup.UseInMemoryStorage();
        });
        services.AddSingleton<IAzureServiceBusClientPool>(_ =>
        {
            clientsCreated++;
            return Substitute.For<IAzureServiceBusClientPool>();
        });
        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<MessagingConfigurationException>()
            .WithMessage($"*'{PriceCacheModule.Identity}'*'Azure Service Bus'*");
        clientsCreated.Should().Be(0);
    }

    private string _SubscriptionName => AzureServiceBusConsumerClientFactory.BusSubscriptionName(_Request(_instanceId));

    private static ConsumerClientRequest _Request(Guid instanceId) =>
        new(_Identity, 1, MessageLane.Bus, ConsumerSubscriptionKind.EveryInstance, instanceId);

    private AzureServiceBusConsumerClientFactory _CreateFactory(
        bool autoProvision = true,
        TimeProvider? timeProvider = null
    ) =>
        new(
            NullLoggerFactory.Instance,
            Options.Create(
                new AzureServiceBusMessagingOptions
                {
                    ConnectionString =
                        "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey",
                    TopicPath = _TopicPath,
                    AutoProvision = autoProvision,
                }
            ),
            _Services(timeProvider),
            _pool
        );

    private static ServiceProvider _Services(TimeProvider? timeProvider)
    {
        var services = new ServiceCollection();

        if (timeProvider is not null)
        {
            services.AddSingleton(timeProvider);
        }

        return services.BuildServiceProvider();
    }

    private static Task _RaiseProcessorErrorAsync(IConsumerClient client, Exception exception)
    {
        var handler = typeof(AzureServiceBusConsumerClient).GetMethod(
            "_ServiceBusProcessor_ProcessErrorAsync",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(ProcessErrorEventArgs)],
            null
        )!;
        var args = new ProcessErrorEventArgs(
            exception,
            ServiceBusErrorSource.Receive,
            "mynamespace.servicebus.windows.net",
            _TopicPath,
            AbortToken
        );

        return (Task)handler.Invoke(client, [args])!;
    }
}

public sealed record PriceChanged(string Sku);

public sealed class PriceCache : IConsume<PriceChanged>
{
    public ValueTask ConsumeAsync(ConsumeContext<PriceChanged> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

public sealed class PriceCacheModule : IMessagingModule
{
    public const string Identity = "pricing.price-cache";

    public static void Register(MessagingCatalogBuilder catalog)
    {
        catalog.AddBusConsumer<PriceCache, PriceChanged>(
            Identity,
            everyInstance: true,
            static (services, context, cancellationToken) =>
                ActivatorUtilities
                    .CreateInstance<PriceCache>(services)
                    .ConsumeAsync((ConsumeContext<PriceChanged>)context, cancellationToken)
        );
    }
}
