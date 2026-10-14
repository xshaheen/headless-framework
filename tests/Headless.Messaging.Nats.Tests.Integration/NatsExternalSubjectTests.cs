// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Headless.Messaging;
using Headless.Messaging.Nats;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// A contract lane bound to a subject a foreign service owns, outside <c>headless.{lane}.{name}</c>, consumed through the
/// normal consumer pipeline and published to by Headless.
/// </summary>
[Collection("Nats")]
public sealed class NatsExternalSubjectTests(NatsFixture fixture) : TestBase
{
    private static readonly TimeSpan _Wait = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task should_consume_a_headerless_message_on_an_owned_stream_with_the_tenant_from_the_subject()
    {
        // given
        var scope = TestScope.Create();
        await using var host = await _StartHostAsync(
            scope,
            streams => streams.Own(scope.Stream, s => s.Subjects(scope.StreamSubjects))
        );
        var subject = scope.Subject("t1", "r9");

        // when
        await _PublishNativeAsync(subject, """{"value":"done"}""");

        // then
        await host.Consumer.WaitForCountAsync(1, _Wait, AbortToken);
        var received = host.Consumer.Received.Single();
        received.TenantId.Should().Be("t1");
        received.TransportAddress.Should().Be(subject);
        received.MessageName.Should().Be(scope.MessageName);
        received.MessageId.Should().MatchRegex($"^nats:{scope.Stream}:1:[0-9]+$");
        received.Value.Should().Be("done");
    }

    [Fact]
    public async Task should_hand_a_raw_json_consumer_the_foreign_bytes_unbound()
    {
        // given: a host whose JSON options would reject this snake_case body if they bound it
        var scope = TestScope.Create();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<RawRunConsumer>();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseNats(options =>
            {
                options.Servers = fixture.ConnectionString;
                options.Streams.Own(scope.Stream, s => s.Subjects(scope.StreamSubjects));
            });
            setup.UseInMemoryStorage();
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
            setup.Options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging
                .Message<RawJson>(scope.MessageName)
                .OnQueue(queue =>
                    queue
                        .RawPayload()
                        .UseNats(nats => nats.ExternalSubject(scope.Filter, s => s.TenantFromSubjectToken(1)))
                );
            messaging.AddModule<RawRunModule>();
        });
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        const string json = """{ "run_id" : "r4",  "note":"café \"q\"" }""";

        // when
        await _PublishNativeAsync(scope.Subject("t3", "r4"), json);

        // then
        var consumer = provider.GetRequiredService<RawRunConsumer>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(_Wait);
        var (tenantId, body) = await consumer.Received.Reader.ReadAsync(timeout.Token);
        tenantId.Should().Be("t3");
        body.Should().Be(json);
    }

    [Fact]
    public async Task should_consume_on_a_bound_stream_and_leave_its_configuration_unchanged_under_reconcile()
    {
        // given: a stream someone else manages, created before the application starts
        var scope = TestScope.Create();
        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        await js.CreateStreamAsync(
            new StreamConfig(scope.Stream, [scope.StreamSubjects])
            {
                Retention = StreamConfigRetention.Limits,
                Storage = StreamConfigStorage.Memory,
                MaxAge = TimeSpan.FromHours(3),
                DuplicateWindow = TimeSpan.FromMinutes(1),
            },
            AbortToken
        );
        var before = (await js.GetStreamAsync(scope.Stream, cancellationToken: AbortToken)).Info.Config;

        // when: the host starts, a foreign producer publishes, and Headless publishes to the bound stream
        await using var host = await _StartHostAsync(
            scope,
            streams => streams.Bind(scope.Stream, scope.StreamSubjects),
            NatsStreamProvisioning.Reconcile
        );
        await _PublishNativeAsync(scope.Subject("t1", "r1"), """{"value":"foreign"}""");
        await host
            .Services.GetRequiredService<IQueue>()
            .EnqueueAsync(new ExternalRun("headless", "t2", "r2"), AbortToken);

        // then
        await host.Consumer.WaitForCountAsync(2, _Wait, AbortToken);
        host.Consumer.Received.Select(r => (r.Value, r.TenantId, r.TransportAddress))
            .Should()
            .BeEquivalentTo([
                ("foreign", (string?)"t1", (string?)scope.Subject("t1", "r1")),
                ("headless", "t2", scope.Subject("t2", "r2")),
            ]);

        var after = (await js.GetStreamAsync(scope.Stream, cancellationToken: AbortToken)).Info.Config;
        after.Subjects.Should().BeEquivalentTo(before.Subjects);
        after.Retention.Should().Be(before.Retention);
        after.Storage.Should().Be(before.Storage);
        after.MaxAge.Should().Be(before.MaxAge);
        after.DuplicateWindow.Should().Be(before.DuplicateWindow);
        after.MaxBytes.Should().Be(before.MaxBytes);
        after.NumReplicas.Should().Be(before.NumReplicas);
    }

    [Fact]
    public async Task should_publish_to_the_subject_the_selector_computes()
    {
        // given
        var scope = TestScope.Create();
        var connection = await fixture.GetConnectionAsync();
        await using var subscription = await connection.SubscribeCoreAsync<byte[]>(
            $"{scope.Root}.>",
            cancellationToken: AbortToken
        );
        await connection.PingAsync(AbortToken);
        await using var host = await _StartHostAsync(
            scope,
            streams => streams.Own(scope.Stream, s => s.Subjects(scope.StreamSubjects)),
            consume: false
        );

        // when
        await host
            .Services.GetRequiredService<IQueue>()
            .EnqueueAsync(new ExternalRun("published", "t7", "r3"), AbortToken);

        // then
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(_Wait);
        var msg = await subscription.Msgs.ReadAsync(timeout.Token);
        msg.Subject.Should().Be(scope.Subject("t7", "r3"));
        msg.Headers.Should().NotBeNull();
        msg.Headers!.ContainsKey(Headers.MessageId).Should().BeTrue();
        msg.Headers.Keys.Should().NotContain(key => key.StartsWith("headless-nats-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_refuse_a_durable_publish_without_a_payload_before_storing_it()
    {
        // given
        var scope = TestScope.Create();
        await using var host = await _StartHostAsync(
            scope,
            streams => streams.Own(scope.Stream, s => s.Subjects(scope.StreamSubjects)),
            consume: false
        );

        // when
        var act = () =>
            host
                .Services.GetRequiredService<IQueue>()
                .EnqueueAsync<ExternalRun>(null, new QueueOptions { DeliveryMode = DeliveryMode.Durable }, AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*'{scope.MessageName}'*Queue lane*'{scope.Filter}'*PublishTo*payload*");
    }

    [Fact]
    public async Task should_fail_startup_when_no_declared_stream_covers_the_filter()
    {
        // given: a declared stream that carries only one tenant's subjects
        var scope = TestScope.Create();

        // when
        var act = () =>
            _StartHostAsync(
                scope,
                streams => streams.Own(scope.Stream, s => s.Subjects($"{scope.Root}.t1.runs.*.result"))
            );

        // then: startup names the contract and the filter, and nothing was created on the broker
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*'{scope.Filter}'*'{scope.MessageName}'*Queue lane*no stream declared*");

        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        var lookup = () => js.GetStreamAsync(scope.Stream, cancellationToken: AbortToken).AsTask();
        await lookup.Should().ThrowAsync<NatsJSApiException>();
    }

    private async Task<ExternalHost> _StartHostAsync(
        TestScope scope,
        Action<NatsStreamCatalog> declareStreams,
        NatsStreamProvisioning provisioning = NatsStreamProvisioning.Verify,
        bool consume = true
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ExternalRunConsumer>();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseNats(options =>
            {
                options.Servers = fixture.ConnectionString;
                options.StreamProvisioning = provisioning;
                declareStreams(options.Streams);
            });
            setup.UseInMemoryStorage();
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging
                .Message<ExternalRun>(scope.MessageName, "1")
                .OnQueue(queue =>
                    queue
                        .RawPayload()
                        .UseNats(nats =>
                            nats.ExternalSubject(
                                scope.Filter,
                                s => s.PublishTo(r => scope.Subject(r.Tenant, r.Run)).TenantFromSubjectToken(1)
                            )
                        )
                );

            if (consume)
            {
                messaging.AddModule<ExternalRunModule>();
            }
        });

        var provider = services.BuildServiceProvider();

        try
        {
            await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
            var host = new ExternalHost(provider);

            if (consume)
            {
                var durable = NatsConsumerClient.BuildDurableName(
                    scope.MessageName,
                    scope.MessageName,
                    MessageLane.Queue
                );
                var js = new NatsJSContext(await fixture.GetConnectionAsync());
                await _WaitUntilAsync(
                    async () =>
                    {
                        try
                        {
                            var consumer = await js.GetConsumerAsync(scope.Stream, durable, AbortToken);
                            consumer.Info.Config.FilterSubject.Should().Be(scope.Filter);
                            return true;
                        }
                        catch (NatsJSApiException e) when (e.Error.Code == 404)
                        {
                            return false;
                        }
                    },
                    $"durable '{durable}' exists"
                );
            }

            return host;
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }

    private async Task _PublishNativeAsync(string subject, string json)
    {
        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        var ack = await js.PublishAsync(subject, Encoding.UTF8.GetBytes(json), cancellationToken: AbortToken);
        ack.EnsureSuccess();
    }

    private static async Task _WaitUntilAsync(Func<Task<bool>> condition, string description)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(_Wait);

        while (!await condition())
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }
            catch (OperationCanceledException e) when (!AbortToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out waiting until {description}.", e);
            }
        }
    }

    // Every test owns a subject root of its own: JetStream refuses two streams with overlapping subjects on one server.
    private sealed record TestScope(string Root, string MessageName, string Stream)
    {
        public static TestScope Create()
        {
            var id = Guid.NewGuid().ToString("N");
            return new TestScope($"acme{id}", $"ext-{id}", $"ACME_{id}");
        }

        public string Filter => $"{Root}.*.runs.*.result";

        public string StreamSubjects => $"{Root}.*.runs.>";

        public string Subject(string tenant, string run) => $"{Root}.{tenant}.runs.{run}.result";
    }

    private sealed class ExternalHost(ServiceProvider services) : IAsyncDisposable
    {
        public IServiceProvider Services => services;

        public ExternalRunConsumer Consumer { get; } = services.GetRequiredService<ExternalRunConsumer>();

        public ValueTask DisposeAsync() => services.DisposeAsync();
    }

    public sealed record ExternalRun(
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("tenant")] string Tenant = "",
        [property: JsonPropertyName("run")] string Run = ""
    );

    public sealed record ExternalRunReceipt(
        string MessageId,
        string MessageName,
        string? TenantId,
        string? TransportAddress,
        string Value
    );

    public sealed class ExternalRunModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddQueueConsumer<ExternalRunConsumer, ExternalRun>(
                "tests.nats.external-run",
                TestConsumerDispatch.FromServices<ExternalRunConsumer, ExternalRun>()
            );
    }

    public sealed class ExternalRunConsumer : IConsume<ExternalRun>
    {
        public ConcurrentQueue<ExternalRunReceipt> Received { get; } = new();

        public ValueTask ConsumeAsync(ConsumeContext<ExternalRun> context, CancellationToken cancellationToken)
        {
            Received.Enqueue(
                new ExternalRunReceipt(
                    context.MessageId,
                    context.MessageName,
                    context.TenantId,
                    context.TransportAddress,
                    context.Message.Value
                )
            );
            return ValueTask.CompletedTask;
        }

        public async Task WaitForCountAsync(int count, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);

            while (Received.Count < count)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), deadline.Token);
            }
        }
    }

    public sealed class RawRunModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddQueueConsumer<RawRunConsumer, RawJson>(
                "tests.nats.raw-run",
                TestConsumerDispatch.FromServices<RawRunConsumer, RawJson>()
            );
    }

    public sealed class RawRunConsumer : IConsume<RawJson>
    {
        public Channel<(string? TenantId, string Body)> Received { get; } =
            Channel.CreateUnbounded<(string? TenantId, string Body)>();

        public ValueTask ConsumeAsync(ConsumeContext<RawJson> context, CancellationToken cancellationToken) =>
            Received.Writer.WriteAsync((context.TenantId, context.Message.ToString()), cancellationToken);
    }
}
