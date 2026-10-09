// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Nats;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
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

    [Theory]
    [InlineData("orders.created", "orders")]
    [InlineData("orders.us.east.created", "orders")]
    [InlineData("orders", "orders")]
    public void should_take_the_first_name_segment_as_the_stream_key(string messageName, string expected)
    {
        NatsStreamProvisioner.StreamKey(messageName).Should().Be(expected);
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
    public async Task should_fail_fast_for_the_configuration_back_off_when_ensure_for_publish_hits_a_configuration_fault()
    {
        // given - the first create fails, later ones succeed
        var js = _CreateJetStreamWithoutStreams();
        var calls = 0;
        js.CreateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw new InvalidOperationException("stream diverged");
                }

                return new ValueTask<INatsJSStream>(_CreateStream(call.Arg<StreamConfig>()));
            });
        var clock = new FakeTimeProvider();
        var provisioner = _CreateProvisioner(NatsStreamProvisioning.Verify, clock);
        var publish = () => provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);
        await publish.Should().ThrowAsync<InvalidOperationException>();

        // when - a publish inside the configuration back-off reports the cached failure without a broker request
        clock.Advance(TimeSpan.FromSeconds(29));
        await publish.Should().ThrowAsync<InvalidOperationException>().WithMessage("stream diverged");
        calls.Should().Be(1);

        // then - the first publish after the back-off tries again
        clock.Advance(TimeSpan.FromSeconds(2));
        await publish();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task should_retry_after_a_short_back_off_when_ensure_for_publish_hits_a_transient_fault()
    {
        // given - the first create meets a JetStream that is briefly unavailable
        var js = _CreateJetStreamWithoutStreams();
        var calls = 0;
        js.CreateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Interlocked.Increment(ref calls) == 1
                    ? throw new NatsJSApiException(new ApiError { Code = 503, Description = "JetStream not ready" })
                    : new ValueTask<INatsJSStream>(_CreateStream(call.Arg<StreamConfig>()))
            );
        var clock = new FakeTimeProvider();
        var provisioner = _CreateProvisioner(NatsStreamProvisioning.Verify, clock);
        var publish = () => provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);
        await publish.Should().ThrowAsync<NatsJSApiException>();

        // when - just over a second passes
        clock.Advance(TimeSpan.FromMilliseconds(1100));
        await publish();

        // then
        calls.Should().Be(2);
    }

    [Fact]
    public async Task should_use_the_configuration_back_off_when_jetstream_refuses_the_stream()
    {
        // given - JetStream refuses the create, as it does for subjects another stream already carries
        var js = _CreateJetStreamWithoutStreams();
        var calls = 0;
        js.CreateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<INatsJSStream>>(_ =>
            {
                Interlocked.Increment(ref calls);
                throw new NatsJSApiException(new ApiError { Code = 400, ErrCode = 10065 });
            });
        var clock = new FakeTimeProvider();
        var provisioner = _CreateProvisioner(NatsStreamProvisioning.Verify, clock);
        var publish = () => provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);
        await publish.Should().ThrowAsync<NatsJSApiException>();

        // when - a transient back-off would have ended, the configuration one has not
        clock.Advance(TimeSpan.FromSeconds(5));
        await publish.Should().ThrowAsync<NatsJSApiException>();

        // then
        calls.Should().Be(1);
    }

    [Fact]
    public async Task should_double_the_back_off_when_ensure_for_publish_fails_again()
    {
        // given - every create fails
        var js = _CreateJetStreamWithoutStreams();
        var calls = 0;
        js.CreateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<INatsJSStream>>(_ =>
            {
                Interlocked.Increment(ref calls);
                throw new InvalidOperationException("stream diverged");
            });
        var clock = new FakeTimeProvider();
        var provisioner = _CreateProvisioner(NatsStreamProvisioning.Verify, clock);
        var publish = () => provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);
        await publish.Should().ThrowAsync<InvalidOperationException>();
        clock.Advance(TimeSpan.FromSeconds(31));
        await publish.Should().ThrowAsync<InvalidOperationException>();

        // when - 31 seconds pass again, inside the doubled 60-second back-off
        clock.Advance(TimeSpan.FromSeconds(31));
        await publish.Should().ThrowAsync<InvalidOperationException>();

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
    public async Task should_retry_after_the_back_off_when_ensure_for_publish_outlives_the_stream_create_timeout()
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
        var clock = new FakeTimeProvider();
        var provisioner = new NatsStreamProvisioner(
            MsOptions.Options.Create(
                new NatsMessagingOptions
                {
                    StreamProvisioning = NatsStreamProvisioning.Verify,
                    StreamCreateTimeout = TimeSpan.FromMilliseconds(50),
                }
            ),
            clock
        );

        // when - the timeout surfaces as a cancellation the caller did not request, which the transport fails
        var publish = () => provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);
        await publish.Should().ThrowAsync<OperationCanceledException>();
        clock.Advance(TimeSpan.FromMinutes(1));
        await publish();

        // then - the timed-out ensure was not remembered as done
        lookups.Should().Be(2);
        await js.Received(1).CreateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_back_off_the_shared_ensure_when_only_the_caller_cancelled()
    {
        // given - the first stream lookup is slow, and the first caller gives up
        var js = _CreateJetStreamWithoutStreams();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        js.GetStreamAsync(Arg.Any<string>(), Arg.Any<StreamInfoRequest?>(), Arg.Any<CancellationToken>())
            .Returns(_ => _MissingAfterAsync(gate.Task));
        var provisioner = _CreateProvisioner(NatsStreamProvisioning.Verify);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var cancelled = provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, cts.Token);
        await cts.CancelAsync();
        var waitCancelled = async () => await cancelled;
        await waitCancelled.Should().ThrowAsync<OperationCanceledException>();

        // when - the shared ensure completes for the next caller
        gate.SetResult();
        var state = await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);

        // then
        state.Should().NotBeNull();
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

    [Fact]
    public void should_resolve_a_message_to_the_declared_stream_that_covers_its_subject()
    {
        // given
        var options = new NatsMessagingOptions();
        options.Streams.Own("ORDERS", stream => stream.Subjects("headless.queue.orders.>"));
        var provisioner = _CreateProvisioner(options);

        // then - a covered name uses the declared stream; others keep the derived one
        provisioner.StreamName(MessageLane.Queue, "orders.placed").Should().Be("ORDERS");
        provisioner.StreamName(MessageLane.Bus, "orders.placed").Should().Be("headless-bus-orders");
        provisioner.StreamName(MessageLane.Queue, "payments.settled").Should().Be("headless-queue-payments");
    }

    [Fact]
    public async Task should_create_a_declared_owned_stream_with_its_subjects_limits_retention_and_default_max_age()
    {
        // given
        var js = _CreateJetStreamWithoutStreams();
        var options = new NatsMessagingOptions();
        options.Streams.Own("ORDERS", stream => stream.Subjects("headless.bus.orders.>").MaxBytes(1024));
        var provisioner = _CreateProvisioner(options);

        // when
        await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);

        // then
        await js.Received(1)
            .CreateStreamAsync(
                Arg.Is<StreamConfig>(config =>
                    config.Name == "ORDERS"
                    && config.Retention == StreamConfigRetention.Limits
                    && config.MaxAge == TimeSpan.FromDays(7)
                    && config.MaxBytes == 1024
                    && config.Subjects!.SequenceEqual(new[] { "headless.bus.orders.>" })
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_bound_a_derived_stream_by_the_default_max_age()
    {
        // given
        var js = _CreateJetStreamWithoutStreams();
        var provisioner = _CreateProvisioner(new NatsMessagingOptions { DefaultStreamMaxAge = TimeSpan.FromHours(1) });

        // when
        await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);

        // then
        await js.Received(1)
            .CreateStreamAsync(
                Arg.Is<StreamConfig>(config => config.MaxAge == TimeSpan.FromHours(1)),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_leave_the_age_unlimited_when_a_declared_stream_sets_max_age_zero()
    {
        // given
        var js = _CreateJetStreamWithoutStreams();
        var options = new NatsMessagingOptions();
        options.Streams.Own("ORDERS", stream => stream.Subjects("headless.bus.orders.>").MaxAge(TimeSpan.Zero));
        var provisioner = _CreateProvisioner(options);

        // when
        await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);

        // then
        await js.Received(1)
            .CreateStreamAsync(
                Arg.Is<StreamConfig>(config => config.MaxAge == TimeSpan.Zero),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_create_a_declared_owned_stream_with_its_duplicate_window()
    {
        // given
        var js = _CreateJetStreamWithoutStreams();
        var options = new NatsMessagingOptions();
        options.Streams.Own(
            "ORDERS",
            stream => stream.Subjects("headless.bus.orders.>").DuplicateWindow(TimeSpan.FromMinutes(30))
        );
        var provisioner = _CreateProvisioner(options);

        // when
        await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);

        // then
        await js.Received(1)
            .CreateStreamAsync(
                Arg.Is<StreamConfig>(config => config.DuplicateWindow == TimeSpan.FromMinutes(30)),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(10, 2)] // the default window
    [InlineData(1, 1)] // shrunk to a shorter age limit, which JetStream requires
    [InlineData(0, 2)] // no age limit leaves the default window
    public async Task should_give_a_derived_stream_the_default_duplicate_window_within_its_max_age(
        int maxAgeMinutes,
        int expectedWindowMinutes
    )
    {
        // given
        var js = _CreateJetStreamWithoutStreams();
        var provisioner = _CreateProvisioner(
            new NatsMessagingOptions { DefaultStreamMaxAge = TimeSpan.FromMinutes(maxAgeMinutes) }
        );

        // when
        await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);

        // then
        await js.Received(1)
            .CreateStreamAsync(
                Arg.Is<StreamConfig>(config => config.DuplicateWindow == TimeSpan.FromMinutes(expectedWindowMinutes)),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_fail_when_a_declared_duplicate_window_exceeds_the_default_max_age()
    {
        // given — the declaration sets a window but leaves the age to DefaultStreamMaxAge
        var js = _CreateJetStreamWithoutStreams();
        var options = new NatsMessagingOptions { DefaultStreamMaxAge = TimeSpan.FromMinutes(1) };
        options.Streams.Own(
            "ORDERS",
            stream => stream.Subjects("headless.bus.orders.>").DuplicateWindow(TimeSpan.FromMinutes(5))
        );
        var provisioner = _CreateProvisioner(options);

        // when
        var act = () => provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*duplicate window*MaxAge*");
        await js.DidNotReceive().CreateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_fail_under_verify_when_an_existing_stream_has_another_duplicate_window()
    {
        // given - a stream whose duplicate window someone else chose
        var js = Substitute.For<INatsJSContext>();
        var live = _CreateStream(
            new StreamConfig
            {
                Name = "headless-bus-orders",
                Subjects = ["headless.bus.orders.>"],
                Storage = StreamConfigStorage.File,
                Retention = StreamConfigRetention.Interest,
                NoAck = false,
                MaxAge = TimeSpan.FromDays(7),
                DuplicateWindow = TimeSpan.FromHours(1),
            }
        );
        js.GetStreamAsync("headless-bus-orders", Arg.Any<StreamInfoRequest?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<INatsJSStream>(live));
        var provisioner = _CreateProvisioner(NatsStreamProvisioning.Verify);

        // when
        var act = () => provisioner.EnsureAsync(js, MessageLane.Bus, ["orders.placed"], _NoShards, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DuplicateWindow*");
    }

    [Fact]
    public async Task should_fail_under_verify_when_an_existing_stream_lacks_the_default_max_age()
    {
        // given - a stream created without an age limit
        var js = Substitute.For<INatsJSContext>();
        var live = _CreateStream(
            new StreamConfig
            {
                Name = "headless-bus-orders",
                Subjects = ["headless.bus.orders.>"],
                Storage = StreamConfigStorage.File,
                Retention = StreamConfigRetention.Interest,
                NoAck = false,
            }
        );
        js.GetStreamAsync("headless-bus-orders", Arg.Any<StreamInfoRequest?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<INatsJSStream>(live));
        var provisioner = _CreateProvisioner(NatsStreamProvisioning.Verify);

        // when
        var act = () => provisioner.EnsureAsync(js, MessageLane.Bus, ["orders.placed"], _NoShards, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*MaxAge*");
    }

    [Fact]
    public async Task should_apply_the_declared_configure_callback_before_stream_options()
    {
        // given
        var js = _CreateJetStreamWithoutStreams();
        var options = new NatsMessagingOptions
        {
            StreamOptions = config => config.NumReplicas = config.NumReplicas == 3 ? 5 : 1,
        };
        options.Streams.Own(
            "ORDERS",
            stream => stream.Subjects("headless.bus.orders.>").Configure(config => config.NumReplicas = 3)
        );
        var provisioner = _CreateProvisioner(options);

        // when
        await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);

        // then
        await js.Received(1)
            .CreateStreamAsync(Arg.Is<StreamConfig>(config => config.NumReplicas == 5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_refuse_a_configure_callback_that_changes_declared_subjects()
    {
        // given
        var js = _CreateJetStreamWithoutStreams();
        var options = new NatsMessagingOptions();
        options.Streams.Own(
            "ORDERS",
            stream => stream.Subjects("headless.bus.orders.>").Configure(config => config.Subjects = ["other.>"])
        );
        var provisioner = _CreateProvisioner(options);

        // when
        var act = () => provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'ORDERS' identity*");
        await js.DidNotReceive().CreateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_never_change_a_bound_stream_and_report_its_consumers()
    {
        // given - a stream managed outside the application, with settings Headless would never choose
        var js = Substitute.For<INatsJSContext>();
        var live = _CreateStream(
            new StreamConfig
            {
                Name = "ORDERS",
                Subjects = ["headless.bus.orders.>"],
                Storage = StreamConfigStorage.Memory,
                Retention = StreamConfigRetention.Limits,
            },
            consumers: 2
        );
        js.GetStreamAsync("ORDERS", Arg.Any<StreamInfoRequest?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<INatsJSStream>(live));
        var options = new NatsMessagingOptions { StreamProvisioning = NatsStreamProvisioning.Reconcile };
        options.Streams.Bind("ORDERS", "headless.bus.orders.>");
        var provisioner = _CreateProvisioner(options);

        // when
        var state = await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);
        await provisioner.EnsureAsync(js, MessageLane.Bus, ["orders.placed"], _NoShards, AbortToken);

        // then
        state.Should().Be(new NatsStreamState("ORDERS", StreamConfigRetention.Limits, 2));
        await js.DidNotReceive().CreateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>());
        await js.DidNotReceive().UpdateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_fail_without_creating_it_when_a_bound_stream_is_missing()
    {
        // given
        var js = _CreateJetStreamWithoutStreams();
        var options = new NatsMessagingOptions();
        options.Streams.Bind("ORDERS", "headless.bus.orders.>");
        var provisioner = _CreateProvisioner(options);

        // when
        var act = () => provisioner.EnsureAsync(js, MessageLane.Bus, ["orders.placed"], _NoShards, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'ORDERS'*Bind*does not exist*");
        await js.DidNotReceive().CreateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_fail_when_a_bound_stream_does_not_carry_its_declared_subjects()
    {
        // given
        var js = Substitute.For<INatsJSContext>();
        var live = _CreateStream(new StreamConfig { Name = "ORDERS", Subjects = ["headless.bus.orders.placed"] });
        js.GetStreamAsync("ORDERS", Arg.Any<StreamInfoRequest?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<INatsJSStream>(live));
        var options = new NatsMessagingOptions();
        options.Streams.Bind("ORDERS", "headless.bus.orders.>");
        var provisioner = _CreateProvisioner(options);

        // when
        var act = () => provisioner.EnsureAsync(js, MessageLane.Bus, ["orders.placed"], _NoShards, AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not carry headless.bus.orders.>*");
    }

    [Fact]
    public async Task should_refuse_a_bound_work_queue_stream_for_bus_messages()
    {
        // given
        var js = Substitute.For<INatsJSContext>();
        var live = _CreateStream(
            new StreamConfig
            {
                Name = "ORDERS",
                Subjects = ["headless.bus.orders.>"],
                Retention = StreamConfigRetention.Workqueue,
            }
        );
        js.GetStreamAsync("ORDERS", Arg.Any<StreamInfoRequest?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<INatsJSStream>(live));
        var options = new NatsMessagingOptions();
        options.Streams.Bind("ORDERS", "headless.bus.orders.>");
        var provisioner = _CreateProvisioner(options);

        // when
        var act = () => provisioner.EnsureAsync(js, MessageLane.Bus, ["orders.placed"], _NoShards, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*work-queue*");
    }

    [Fact]
    public async Task should_ensure_a_declared_stream_once_for_every_name_it_covers_at_consumer_startup()
    {
        // given
        var js = _CreateJetStreamWithoutStreams();
        var options = new NatsMessagingOptions();
        options.Streams.Own("ORDERS", stream => stream.Subjects("headless.queue.orders.>"));
        var provisioner = _CreateProvisioner(options);

        // when
        await provisioner.EnsureAsync(
            js,
            MessageLane.Queue,
            ["orders.placed", "orders.cancelled", "payments.settled"],
            _NoShards,
            AbortToken
        );

        // then
        await js.Received(1)
            .CreateStreamAsync(Arg.Is<StreamConfig>(config => config.Name == "ORDERS"), Arg.Any<CancellationToken>());
        await js.Received(1)
            .CreateStreamAsync(
                Arg.Is<StreamConfig>(config => config.Name == "headless-queue-payments"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_warm_up_only_owned_declared_streams_and_share_the_result_with_publishes()
    {
        // given
        var js = _CreateJetStreamWithoutStreams();
        var options = new NatsMessagingOptions();
        options.Streams.Own("ORDERS", stream => stream.Subjects("headless.bus.orders.>"));
        options.Streams.Bind("PAYMENTS", "headless.bus.payments.>");
        var provisioner = _CreateProvisioner(options);

        // when
        await provisioner.WarmUpAsync(js, AbortToken);
        await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);

        // then
        provisioner.HasOwnedDeclaredStreams.Should().BeTrue();
        await js.Received(1).CreateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>());
        await js.DidNotReceive()
            .GetStreamAsync("PAYMENTS", Arg.Any<StreamInfoRequest?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_back_off_publishes_when_the_warm_up_failed()
    {
        // given - the warm-up create fails, the publish create succeeds
        var js = _CreateJetStreamWithoutStreams();
        var calls = 0;
        js.CreateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Interlocked.Increment(ref calls) == 1
                    ? throw new InvalidOperationException("broker not ready")
                    : new ValueTask<INatsJSStream>(_CreateStream(call.Arg<StreamConfig>()))
            );
        var options = new NatsMessagingOptions();
        options.Streams.Own("ORDERS", stream => stream.Subjects("headless.bus.orders.>"));
        var provisioner = _CreateProvisioner(options);
        var warmUp = () => provisioner.WarmUpAsync(js, AbortToken);
        await warmUp.Should().ThrowAsync<InvalidOperationException>();

        // when
        await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);

        // then
        calls.Should().Be(2);
    }

    private static NatsStreamProvisioner _CreateProvisioner(NatsStreamProvisioning mode, TimeProvider? clock = null) =>
        new(MsOptions.Options.Create(new NatsMessagingOptions { StreamProvisioning = mode }), clock);

    private static NatsStreamProvisioner _CreateProvisioner(NatsMessagingOptions options) =>
        new(MsOptions.Options.Create(options));

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

    private static async ValueTask<INatsJSStream> _MissingAfterAsync(Task gate)
    {
        await gate;

        throw new NatsJSApiException(new ApiError { Code = 404, ErrCode = 10059 });
    }

    private static async ValueTask<INatsJSStream> _NeverAnswerAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);

        throw new InvalidOperationException("The delay ends only by cancellation.");
    }

    private static INatsJSStream _CreateStream(StreamConfig config, long consumers = 0)
    {
        var stream = Substitute.For<INatsJSStream>();
        stream.Info.Returns(
            new StreamInfo
            {
                Config = config,
                State = new StreamState { ConsumerCount = consumers },
            }
        );
        return stream;
    }
}
