// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Headless.Messaging;
using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Sdk;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

/// <summary>
/// RabbitMQ-specific reply channel behavior the shared request/reply suite cannot observe: the broker-side shape and
/// lifetime of the reply queue, sends to a queue that is gone, refusal of non-reply queues, and re-declaration after
/// the queue is lost.
/// </summary>
[Collection<RabbitMqFixture>]
public sealed class RabbitMqReplyTransportTests(RabbitMqFixture fixture) : TestBase
{
    // Generous enough for a broker under test load; each condition is normally met in well under a second.
    private static readonly TimeSpan _Bound = TimeSpan.FromSeconds(20);

    private readonly List<ConnectionChannelPool> _pools = [];

    [Fact]
    public async Task should_hold_an_exclusive_reply_queue_while_the_caller_runs_and_delete_it_when_the_caller_stops()
    {
        // given
        var before = await _ReplyQueueNamesAsync();
        await using var caller = _BuildCaller();
        await caller.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        // when
        RabbitMqBrokerQueue? replyQueue = null;
        await _WaitUntilAsync(
            async () =>
            {
                replyQueue = (await fixture.ListQueuesAsOperatorAsync(AbortToken)).SingleOrDefault(queue =>
                    queue.Name.StartsWith(ReplyAddresses.Prefix, StringComparison.Ordinal)
                    && !before.Contains(queue.Name)
                );
                return replyQueue is not null;
            },
            "the caller declared its reply queue"
        );
        await caller.DisposeAsync();

        // then
        replyQueue!.Exclusive.Should().BeTrue("the broker deletes an exclusive queue with its connection");
        replyQueue.Durable.Should().BeFalse("a reply never outlives the caller that waits for it");
        await _WaitUntilAsync(
            async () => !(await _ReplyQueueNamesAsync()).Contains(replyQueue.Name),
            "the stopped caller's reply queue is gone"
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_drop_a_reply_to_a_deleted_reply_queue_without_an_exception(bool publishConfirms)
    {
        // given
        var transport = _CreateTransport(publishConfirms);
        var gone = await transport.OpenListenerAsync(static (_, _) => ValueTask.CompletedTask, AbortToken);
        var goneAddress = await gone.WaitForAddressAsync(AbortToken);
        await gone.DisposeAsync();
        await _WaitUntilAsync(
            async () => !await fixture.QueueExistsAsync(goneAddress, AbortToken),
            "the closed listener's reply queue is gone"
        );

        var received = Channel.CreateUnbounded<TransportMessage>();
        await using var live = await transport.OpenListenerAsync(
            (reply, _) => received.Writer.WriteAsync(reply, AbortToken),
            AbortToken
        );
        var liveAddress = await live.WaitForAddressAsync(AbortToken);

        // when
        var act = () => transport.SendAsync(goneAddress, _Reply("late"), AbortToken).AsTask();

        // then
        await act.Should().NotThrowAsync();

        // The pooled channel survived the unroutable reply: the next reply still goes out and arrives.
        await transport.SendAsync(liveAddress, _Reply("live"), AbortToken);
        var delivered = await received.Reader.ReadAsync(AbortToken).AsTask().WaitAsync(_Bound, AbortToken);
        delivered.Headers[MessagingHeaders.InReplyTo].Should().Be("live");
    }

    [Fact]
    public async Task should_refuse_a_server_named_or_application_queue_address_without_publishing()
    {
        // given
        var transport = _CreateTransport(publishConfirms: false);
        var connection = await fixture.GetConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: AbortToken);
        var serverNamed = (
            await channel.QueueDeclareAsync(
                string.Empty,
                durable: false,
                exclusive: true,
                autoDelete: false,
                cancellationToken: AbortToken
            )
        ).QueueName;
        var application = $"queue.reply-refusal-{Guid.NewGuid():N}";
        await channel.QueueDeclareAsync(
            application,
            durable: false,
            exclusive: false,
            autoDelete: false,
            cancellationToken: AbortToken
        );

        try
        {
            serverNamed.Should().StartWith("amq.gen-");

            foreach (var address in new[] { serverNamed, application })
            {
                // when
                var act = () => transport.SendAsync(address, _Reply("forged"), AbortToken).AsTask();

                // then
                ((IReplyTransport)transport)
                    .IsReplyAddress(address)
                    .Should()
                    .BeFalse();
                await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(address));
                (await channel.QueueDeclarePassiveAsync(address, AbortToken))
                    .MessageCount.Should()
                    .Be(0, $"no reply was published to '{address}'");
            }
        }
        finally
        {
            await channel.QueueDeleteAsync(serverNamed, ifUnused: false, ifEmpty: false, cancellationToken: AbortToken);
            await channel.QueueDeleteAsync(application, ifUnused: false, ifEmpty: false, cancellationToken: AbortToken);
        }
    }

    [Fact]
    public async Task should_consume_replies_under_a_new_address_after_its_reply_queue_is_lost()
    {
        // given
        var transport = _CreateTransport(publishConfirms: false);
        var received = Channel.CreateUnbounded<TransportMessage>();
        await using var listener = await transport.OpenListenerAsync(
            (reply, _) => received.Writer.WriteAsync(reply, AbortToken),
            AbortToken
        );
        var lost = await listener.WaitForAddressAsync(AbortToken);

        // when
        await fixture.DeleteQueueAsOperatorAsync(lost, AbortToken);

        // then
        var replacement = lost;
        await _WaitUntilAsync(
            async () =>
            {
                replacement = await listener.WaitForAddressAsync(AbortToken);
                return !string.Equals(replacement, lost, StringComparison.Ordinal);
            },
            "the listener re-declared its reply queue under a new address"
        );
        ReplyAddresses.IsInReplyNamespace(replacement).Should().BeTrue();
        (await fixture.QueueExistsAsync(lost, AbortToken)).Should().BeFalse("the old address is not re-declared");

        await transport.SendAsync(replacement, _Reply("after-loss"), AbortToken);
        var delivered = await received.Reader.ReadAsync(AbortToken).AsTask().WaitAsync(_Bound, AbortToken);
        delivered.Headers[MessagingHeaders.InReplyTo].Should().Be("after-loss");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var pool in _pools)
        {
            await pool.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }

    private RabbitMqReplyTransport _CreateTransport(bool publishConfirms)
    {
        var pool = new ConnectionChannelPool(
            NullLogger<ConnectionChannelPool>.Instance,
            Options.Create(new MessagingOptions { Version = "v1" }),
            Options.Create(
                new RabbitMqMessagingOptions
                {
                    HostName = fixture.HostName,
                    Port = fixture.Port,
                    UserName = fixture.UserName,
                    Password = fixture.Password,
                    ExchangeName = $"reply-{Guid.NewGuid():N}",
                    PublishConfirms = publishConfirms,
                }
            )
        );
        _pools.Add(pool);

        return new RabbitMqReplyTransport(pool, TimeProvider.System, NullLogger<RabbitMqReplyTransport>.Instance);
    }

    private ServiceProvider _BuildCaller()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseRabbitMq(options =>
            {
                options.HostName = fixture.HostName;
                options.Port = fixture.Port;
                options.UserName = fixture.UserName;
                options.Password = fixture.Password;
                options.ExchangeName = $"reply-{Guid.NewGuid():N}";
            });
            setup.UseInMemoryStorage();
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
            setup.AddRequestReply();
        });

        return services.BuildServiceProvider();
    }

    private async Task<HashSet<string>> _ReplyQueueNamesAsync()
    {
        return (await fixture.ListQueuesAsOperatorAsync(AbortToken))
            .Select(static queue => queue.Name)
            .Where(static name => name.StartsWith(ReplyAddresses.Prefix, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static TransportMessage _Reply(string inReplyTo)
    {
        return new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [MessagingHeaders.MessageId] = Guid.NewGuid().ToString("D"),
                [MessagingHeaders.InReplyTo] = inReplyTo,
            },
            "{}"u8.ToArray()
        );
    }

    private static async Task _WaitUntilAsync(Func<Task<bool>> condition, string what)
    {
        using var bound = new CancellationTokenSource(_Bound);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(bound.Token, AbortToken);

        try
        {
            while (!await condition())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), linked.Token);
            }
        }
        catch (OperationCanceledException e) when (bound.IsCancellationRequested && !AbortToken.IsCancellationRequested)
        {
            throw new XunitException($"Gave up after {_Bound} waiting until {what}.", e);
        }
    }
}
