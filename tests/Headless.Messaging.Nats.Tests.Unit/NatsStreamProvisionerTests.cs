// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Nats;
using Headless.Testing.Tests;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using MsOptions = Microsoft.Extensions.Options;

namespace Tests;

public sealed class NatsStreamProvisionerTests : TestBase
{
    private static readonly HashSet<string> _NoShards = new(StringComparer.Ordinal);

    [Fact]
    public void should_declare_the_whole_stream_key_when_build_stream_subjects_for_a_prefixed_name()
    {
        NatsStreamProvisioner.BuildStreamSubjects("orders", ["orders.created"], _NoShards).Should().Equal("orders.>");
    }

    [Fact]
    public void should_declare_the_bare_key_only_for_a_name_equal_to_it_when_build_stream_subjects()
    {
        NatsStreamProvisioner.BuildStreamSubjects("orders", ["orders"], _NoShards).Should().Equal("orders");
        NatsStreamProvisioner
            .BuildStreamSubjects("orders", ["orders", "orders.created"], _NoShards)
            .Should()
            .Equal("orders", "orders.>");
    }

    [Fact]
    public void should_ask_for_the_same_subjects_whichever_names_a_host_declares_when_build_stream_subjects()
    {
        // given - a publisher that only knows one message and a consumer that knows two, on one stream key
        var publisher = NatsStreamProvisioner.BuildStreamSubjects("orders", ["orders.placed"], _NoShards);
        var consumer = NatsStreamProvisioner.BuildStreamSubjects(
            "orders",
            ["orders.placed", "orders.cancelled", "orders.placed.eu"],
            new HashSet<string>(StringComparer.Ordinal) { "orders.placed" }
        );

        // then - whoever creates the stream covers the other, so start order cannot fail a host under Verify
        publisher.Should().Equal(consumer);
    }

    [Fact]
    public void should_keep_exact_subjects_when_build_stream_subjects_for_names_the_key_does_not_prefix()
    {
        // given - a custom normalizer that maps unrelated names onto one stream key
        var subjects = NatsStreamProvisioner.BuildStreamSubjects(
            "all",
            ["orders.created", "payments.settled", "payments.settled"],
            new HashSet<string>(StringComparer.Ordinal) { "payments.settled" }
        );

        // then
        subjects.Should().Equal("orders.created", "payments.settled", "payments.settled.>");
    }

    [Fact]
    public void should_cover_every_consumer_filter_with_the_stream_subjects()
    {
        // given
        var sharded = new HashSet<string>(StringComparer.Ordinal) { "orders.created" };
        string[] names = ["orders", "orders.created", "orders.updated"];

        // when
        var streamSubjects = NatsStreamProvisioner.BuildStreamSubjects("orders", names, sharded);
        var consumerSubjects = NatsConsumerClient.BuildConsumerSubjects(names, sharded);

        // then - JetStream delivers nothing to a filter the stream does not carry
        NatsStreamReconciliation.FindUncoveredSubjects(consumerSubjects, [.. streamSubjects]).Should().BeEmpty();
    }

    [Fact]
    public async Task should_create_the_stream_with_the_whole_key_before_the_first_publish()
    {
        // given
        var js = _CreateJetStreamWithoutStreams();
        var provisioner = _CreateProvisioner(NatsStreamProvisioning.Verify);

        // when
        await provisioner.EnsureForPublishAsync(js, MessageLane.Queue, "orders.placed", false, AbortToken);

        // then
        await js.Received(1)
            .CreateStreamAsync(
                Arg.Is<StreamConfig>(config =>
                    config.Name == "headless-queue-orders"
                    && config.Retention == StreamConfigRetention.Workqueue
                    && config.Subjects!.SequenceEqual(new[] { "headless.queue.orders.>" })
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_ensure_once_per_message_when_ensure_for_publish_is_called_repeatedly()
    {
        // given
        var js = _CreateJetStreamWithoutStreams();
        var provisioner = _CreateProvisioner(NatsStreamProvisioning.Verify);

        // when
        await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(_ => provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken))
        );
        await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);

        // then
        await js.Received(1).CreateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_retry_on_the_next_publish_when_ensure_for_publish_failed()
    {
        // given - the first create fails, the second succeeds
        var js = _CreateJetStreamWithoutStreams();
        var calls = 0;
        js.CreateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw new InvalidOperationException("broker unavailable");
                }

                return new ValueTask<INatsJSStream>(_CreateStream(call.Arg<StreamConfig>()));
            });
        var provisioner = _CreateProvisioner(NatsStreamProvisioning.Verify);

        // when
        var first = () => provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);
        await first.Should().ThrowAsync<InvalidOperationException>();
        await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);

        // then
        calls.Should().Be(2);
    }

    [Fact]
    public async Task should_ensure_again_after_forget_published()
    {
        // given
        var js = _CreateJetStreamWithoutStreams();
        var provisioner = _CreateProvisioner(NatsStreamProvisioning.Verify);
        await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);

        // when - the stream vanished after the first ensure, so the transport forgets it
        provisioner.ForgetPublished(MessageLane.Bus, "orders.placed");
        await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);

        // then
        await js.Received(2).CreateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_ensure_the_shard_wildcard_when_a_name_equal_to_its_key_is_later_published_with_a_shard()
    {
        // given - the first publish of a message named exactly its stream key carries no shard
        var js = _CreateJetStreamWithoutStreams();
        var provisioner = _CreateProvisioner(NatsStreamProvisioning.Verify);
        await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders", false, AbortToken);

        // when - a later publish of the same message carries one
        await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders", true, AbortToken);

        // then - the unsharded ensure did not stand in for the shard subject the sharded publish needs
        await js.Received(1)
            .CreateStreamAsync(
                Arg.Is<StreamConfig>(config => config.Subjects!.Contains("headless.bus.orders.>")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_retry_on_the_next_publish_when_ensure_for_publish_outlives_the_stream_create_timeout()
    {
        // given - the first stream lookup never answers, so only StreamCreateTimeout ends it
        var js = _CreateJetStreamWithoutStreams();
        var lookups = 0;
        js.GetStreamAsync(Arg.Any<string>(), Arg.Any<StreamInfoRequest?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Interlocked.Increment(ref lookups) == 1
                    ? _NeverAnswerAsync(call.Arg<CancellationToken>())
                    : throw new NatsJSApiException(new ApiError { Code = 404, ErrCode = 10059 })
            );
        var provisioner = new NatsStreamProvisioner(
            MsOptions.Options.Create(
                new NatsMessagingOptions
                {
                    StreamProvisioning = NatsStreamProvisioning.Verify,
                    StreamCreateTimeout = TimeSpan.FromMilliseconds(50),
                }
            )
        );

        // when - the timeout surfaces as a cancellation the caller did not request, which the transport fails
        var first = () => provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);
        await first.Should().ThrowAsync<OperationCanceledException>();
        await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);

        // then - the timed-out ensure was not remembered
        lookups.Should().Be(2);
        await js.Received(1).CreateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_touch_the_broker_when_stream_provisioning_is_disabled()
    {
        // given
        var js = Substitute.For<INatsJSContext>();
        var provisioner = _CreateProvisioner(NatsStreamProvisioning.Disabled);

        // when
        await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);
        await provisioner.EnsureAsync(js, MessageLane.Bus, ["orders.placed"], _NoShards, AbortToken);

        // then
        js.ReceivedCalls().Should().BeEmpty();
        provisioner.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_under_verify_when_an_existing_stream_does_not_carry_the_key()
    {
        // given - a stream an earlier version created with one exact subject
        var js = Substitute.For<INatsJSContext>();
        var live = _CreateStream(
            new StreamConfig
            {
                Name = "headless-bus-orders",
                Subjects = ["headless.bus.orders.placed"],
                Storage = StreamConfigStorage.File,
                Retention = StreamConfigRetention.Interest,
                NoAck = false,
            }
        );
        js.GetStreamAsync("headless-bus-orders", Arg.Any<StreamInfoRequest?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<INatsJSStream>(live));
        var provisioner = _CreateProvisioner(NatsStreamProvisioning.Verify);

        // when
        var act = () => provisioner.EnsureAsync(js, MessageLane.Bus, ["orders.cancelled"], _NoShards, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*headless.bus.orders.>*");
        await js.DidNotReceive().UpdateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>());
    }

    private static NatsStreamProvisioner _CreateProvisioner(NatsStreamProvisioning mode) =>
        new(MsOptions.Options.Create(new NatsMessagingOptions { StreamProvisioning = mode }));

    private static INatsJSContext _CreateJetStreamWithoutStreams()
    {
        var js = Substitute.For<INatsJSContext>();
        js.GetStreamAsync(Arg.Any<string>(), Arg.Any<StreamInfoRequest?>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<INatsJSStream>>(_ =>
                throw new NatsJSApiException(new ApiError { Code = 404, ErrCode = 10059 })
            );
        js.CreateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<INatsJSStream>(_CreateStream(call.Arg<StreamConfig>())));
        return js;
    }

    private static async ValueTask<INatsJSStream> _NeverAnswerAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);

        throw new InvalidOperationException("The delay ends only by cancellation.");
    }

    private static INatsJSStream _CreateStream(StreamConfig config)
    {
        var stream = Substitute.For<INatsJSStream>();
        stream.Info.Returns(new StreamInfo { Config = config });
        return stream;
    }
}
