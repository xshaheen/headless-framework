// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using MsOptions = Microsoft.Extensions.Options;

namespace Tests;

public sealed class NatsExternalSubjectTests : TestBase
{
    private const string _Name = "runs.result";
    private const string _Filter = "acme.*.runs.*.result";
    private const string _Subject = "acme.t1.runs.r9.result";
    private const string _Stream = "ACME_RUNS";

    private static string _SharedSelector(RunResult result) => $"acme.{result.Tenant}.runs.{result.Run}.result";

    #region Builder

    [Fact]
    public void should_compute_the_publish_subject_as_a_provider_header_when_publish_to_is_declared()
    {
        // given
        var contribution = _Contribution(n =>
            n.ExternalSubject(_Filter, s => s.PublishTo(r => $"acme.{r.Tenant}.runs.{r.Run}.result"))
        );

        // when
        var subject = contribution.Selector(new RunResult("t1", "r9"));

        // then
        contribution.HeaderName.Should().Be(NatsMessagingHeaders.ExternalSubject);
        subject.Should().Be(_Subject);
    }

    [Fact]
    public void should_publish_to_a_literal_filter_when_no_selector_is_declared()
    {
        // given
        var contribution = _Contribution(n => n.ExternalSubject("acme.runs.result"));

        // when
        var subject = contribution.Selector(new RunResult("t1", "r9"));

        // then
        subject.Should().Be("acme.runs.result");
    }

    [Fact]
    public void should_refuse_to_publish_when_the_filter_has_wildcards_and_no_selector()
    {
        // given
        var contribution = _Contribution(n => n.ExternalSubject(_Filter));

        // when
        var act = () => contribution.Selector(new RunResult("t1", "r9"));

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*consume-only*");
    }

    [Fact]
    public void should_refuse_a_null_payload_when_publish_to_computes_the_subject_of_a_wildcard_filter()
    {
        // given
        var config = (IProviderMissingPayloadValidation)_Config(n =>
            n.ExternalSubject(_Filter, s => s.PublishTo(_SharedSelector))
        );

        // when
        var act = () => config.ValidateMissingPayload(_Name, MessageLane.Queue);

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*'{_Name}'*Queue lane*'{_Filter}'*PublishTo*payload*");
    }

    [Fact]
    public void should_refuse_a_null_payload_when_a_wildcard_filter_has_no_publish_to()
    {
        // given
        var config = (IProviderMissingPayloadValidation)_Config(n => n.ExternalSubject(_Filter));

        // when
        var act = () => config.ValidateMissingPayload(_Name, MessageLane.Queue);

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*'{_Name}'*Queue lane*'{_Filter}'*without PublishTo*consume-only*");
    }

    [Theory]
    [InlineData("literal filter with PublishTo")]
    [InlineData("literal filter")]
    public void should_accept_a_null_payload_when_the_filter_is_the_publish_subject(string scenario)
    {
        // given
        var config = (IProviderMissingPayloadValidation)_Config(n =>
            _ = scenario switch
            {
                "literal filter with PublishTo" => n.ExternalSubject(
                    "acme.runs.result",
                    s => s.PublishTo(_ => "acme.runs.result")
                ),
                _ => n.ExternalSubject("acme.runs.result"),
            }
        );

        // when
        var act = () => config.ValidateMissingPayload(_Name, MessageLane.Queue);

        // then
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("acme.*.runs.r9.result", "must be literal")]
    [InlineData("acme.t1.runs.>", "must be literal")]
    [InlineData("acme..runs.r9.result", "must be literal")]
    [InlineData("acme.t 1.runs.r9.result", "must be literal")]
    [InlineData("acme.t1.runs.r9.result\n", "must be literal")]
    [InlineData("", "must be literal")]
    [InlineData("other.t1.runs.r9.result", "does not match")]
    [InlineData("acme.t1.runs.r9.result.extra", "does not match")]
    public void should_reject_a_computed_subject_that_is_not_literal_or_does_not_match(string computed, string reason)
    {
        // given
        var contribution = _Contribution(n => n.ExternalSubject(_Filter, s => s.PublishTo(_ => computed)));

        // when
        var act = () => contribution.Selector(new RunResult("t1", "r9"));

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage($"*{reason}*");
    }

    [Fact]
    public void should_merge_identical_declarations_from_one_shared_method()
    {
        // given
        static void declare(NatsMessageConfigBuilder<RunResult> nats) =>
            nats.ExternalSubject(_Filter, s => s.PublishTo(_SharedSelector).TenantFromSubjectToken(1));

        // when
        var first = _Config(declare);
        var second = _Config(declare);

        // then
        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Theory]
    [InlineData("filter")]
    [InlineData("token")]
    [InlineData("selector")]
    public void should_not_merge_declarations_that_differ(string difference)
    {
        // given
        var first = _Config(n =>
            n.ExternalSubject(_Filter, s => s.PublishTo(_SharedSelector).TenantFromSubjectToken(1))
        );

        // when
        var second = difference switch
        {
            "filter" => _Config(n =>
                n.ExternalSubject("acme.*.runs.>", s => s.PublishTo(_SharedSelector).TenantFromSubjectToken(1))
            ),
            "token" => _Config(n =>
                n.ExternalSubject(_Filter, s => s.PublishTo(_SharedSelector).TenantFromSubjectToken(2))
            ),
            _ => _Config(n => n.ExternalSubject(_Filter, s => s.PublishTo(r => r.Run).TenantFromSubjectToken(1))),
        };

        // then
        first.Should().NotBe(second);
    }

    [Fact]
    public void should_reject_a_negative_tenant_token_index()
    {
        // given
        var builder = new MessageContractBuilder<RunResult>(_Name, "1");

        // when
        var act = () =>
            builder.OnQueue(q => q.UseNats(n => n.ExternalSubject(_Filter, s => s.TenantFromSubjectToken(-1))));

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region Startup validation

    [Fact]
    public void should_accept_a_filter_covered_by_one_declared_stream()
    {
        // given
        var streams = new NatsStreamCatalog().Bind(_Stream, "acme.*.runs.>");

        // when
        var subjects = NatsExternalSubjects.Create(
            [_Route(_Name, MessageLane.Queue, n => n.ExternalSubject(_Filter))],
            streams
        );

        // then
        var binding = subjects.Find(MessageLane.Queue, _Name);
        binding.Should().NotBeNull();
        binding!.Filter.Should().Be(_Filter);
        subjects.Find(MessageLane.Bus, _Name).Should().BeNull();
    }

    [Theory]
    [InlineData("acme..result", "not a valid NATS subject filter")]
    [InlineData("acme.>.result", "not a valid NATS subject filter")]
    [InlineData("acme.run*.result", "not a valid NATS subject filter")]
    [InlineData("acme.runs result", "not a valid NATS subject filter")]
    [InlineData("headless.queue.runs", "headless.>")]
    [InlineData("*.queue.runs", "headless.>")]
    [InlineData("_INBOX.abc", "_INBOX.>")]
    [InlineData("$JS.API.x", "$JS.>")]
    [InlineData("$SYS.x", "$SYS.>")]
    [InlineData(">", "headless.>")]
    public void should_fail_startup_for_an_invalid_or_reserved_filter(string filter, string reason)
    {
        // given
        var streams = new NatsStreamCatalog().Own(_Stream, s => s.Subjects("acme.>"));

        // when
        var act = () =>
            NatsExternalSubjects.Create([_Route(_Name, MessageLane.Queue, n => n.ExternalSubject(filter))], streams);

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*'{filter}'*'{_Name}'*Queue lane*")
            .Which.Message.Should()
            .Contain(reason);
    }

    [Fact]
    public void should_fail_startup_when_no_declared_stream_covers_the_filter()
    {
        // given: a stream that covers part of the filter's subjects is not enough
        var streams = new NatsStreamCatalog().Own(_Stream, s => s.Subjects("acme.t1.runs.*.result"));

        // when
        var act = () =>
            NatsExternalSubjects.Create([_Route(_Name, MessageLane.Queue, n => n.ExternalSubject(_Filter))], streams);

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*'{_Filter}'*'{_Name}'*Queue lane*no stream declared*");
    }

    [Fact]
    public void should_fail_startup_when_the_lane_also_declares_a_subject_shard()
    {
        // given
        var streams = new NatsStreamCatalog().Own(_Stream, s => s.Subjects("acme.>"));

        // when
        var act = () =>
            NatsExternalSubjects.Create(
                [_Route(_Name, MessageLane.Bus, n => n.SubjectShard(r => r.Tenant).ExternalSubject(_Filter))],
                streams
            );

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*Bus lane*SubjectShard*");
    }

    [Fact]
    public void should_fail_startup_when_two_contracts_on_a_lane_have_overlapping_filters()
    {
        // given
        var streams = new NatsStreamCatalog().Own(_Stream, s => s.Subjects("acme.>"));

        // when
        var act = () =>
            NatsExternalSubjects.Create(
                [
                    _Route(_Name, MessageLane.Queue, n => n.ExternalSubject(_Filter)),
                    _Route("runs.any", MessageLane.Queue, n => n.ExternalSubject("acme.t1.runs.>")),
                ],
                streams
            );

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage($"*overlaps '{_Filter}'*'{_Name}'*");
    }

    [Fact]
    public void should_accept_overlapping_filters_on_different_lanes()
    {
        // given
        var streams = new NatsStreamCatalog().Own(_Stream, s => s.Subjects("acme.>"));

        // when
        var subjects = NatsExternalSubjects.Create(
            [
                _Route(_Name, MessageLane.Queue, n => n.ExternalSubject(_Filter)),
                _Route("runs.any", MessageLane.Bus, n => n.ExternalSubject("acme.t1.runs.>")),
            ],
            streams
        );

        // then
        subjects.All.Should().HaveCount(2);
    }

    [Fact]
    public void should_fail_startup_when_the_tenant_token_is_beyond_a_fixed_length_filter()
    {
        // given
        var streams = new NatsStreamCatalog().Own(_Stream, s => s.Subjects("acme.>"));

        // when
        var act = () =>
            NatsExternalSubjects.Create(
                [_Route(_Name, MessageLane.Queue, n => n.ExternalSubject(_Filter, s => s.TenantFromSubjectToken(5)))],
                streams
            );

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*TenantFromSubjectToken(5)*");
    }

    [Fact]
    public void should_fail_startup_when_a_bus_binding_is_covered_by_an_owned_work_queue_stream()
    {
        // given
        var streams = new NatsStreamCatalog().Own(
            _Stream,
            s => s.Subjects("acme.>").Retention(StreamConfigRetention.Workqueue)
        );

        // when
        var act = () =>
            NatsExternalSubjects.Create([_Route(_Name, MessageLane.Bus, n => n.ExternalSubject(_Filter))], streams);

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*'{_Filter}'*'{_Name}'*Bus lane*'{_Stream}'*work-queue*");
    }

    [Theory]
    [InlineData("queue-on-owned-work-queue")]
    [InlineData("bus-on-bound")]
    public void should_accept_a_work_queue_stream_where_startup_cannot_or_need_not_refuse_it(string scenario)
    {
        // given: a Queue lane has one durable per message, and a bound stream's retention is checked at the broker
        var streams = string.Equals(scenario, "bus-on-bound", StringComparison.Ordinal)
            ? new NatsStreamCatalog().Bind(_Stream, "acme.>")
            : new NatsStreamCatalog().Own(
                _Stream,
                s => s.Subjects("acme.>").Retention(StreamConfigRetention.Workqueue)
            );
        var lane = string.Equals(scenario, "bus-on-bound", StringComparison.Ordinal)
            ? MessageLane.Bus
            : MessageLane.Queue;

        // when
        var subjects = NatsExternalSubjects.Create([_Route(_Name, lane, n => n.ExternalSubject(_Filter))], streams);

        // then
        subjects.Find(lane, _Name).Should().NotBeNull();
    }

    [Fact]
    public void should_resolve_the_declared_stream_covering_the_filter()
    {
        // given
        var options = new NatsMessagingOptions();
        options.Streams.Own(_Stream, s => s.Subjects("acme.>"));
        var subjects = NatsExternalSubjects.Create(
            [_Route(_Name, MessageLane.Queue, n => n.ExternalSubject(_Filter))],
            options.Streams
        );
        var provisioner = new NatsStreamProvisioner(MsOptions.Options.Create(options), externalSubjects: subjects);

        // when
        var stream = provisioner.Resolve(MessageLane.Queue, _Name);

        // then
        stream.Name.Should().Be(_Stream);
        provisioner.StreamName(MessageLane.Bus, _Name).Should().Be("headless-bus-runs");
    }

    [Fact]
    public void should_throw_on_every_resolve_when_no_declared_stream_covers_the_external_subject()
    {
        // given - the binding was validated against a catalog the provisioner's options no longer carry
        var covering = new NatsStreamCatalog().Own(_Stream, s => s.Subjects("acme.>"));
        var subjects = NatsExternalSubjects.Create(
            [_Route(_Name, MessageLane.Queue, n => n.ExternalSubject(_Filter))],
            covering
        );
        var provisioner = new NatsStreamProvisioner(
            MsOptions.Options.Create(new NatsMessagingOptions()),
            externalSubjects: subjects
        );

        // when
        var resolve = () => provisioner.Resolve(MessageLane.Queue, _Name);

        // then - a failed resolution is not remembered, so it fails the same way each time
        resolve.Should().Throw<InvalidOperationException>().WithMessage($"*'{_Filter}'*");
        resolve.Should().Throw<InvalidOperationException>().WithMessage($"*'{_Filter}'*");
    }

    #endregion

    #region Publish

    [Fact]
    public void should_publish_to_the_stored_subject_and_keep_it_off_the_wire()
    {
        // given
        var binding = _Binding(MessageLane.Queue, n => n.ExternalSubject(_Filter, s => s.PublishTo(_SharedSelector)));
        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "m-1",
                [Headers.MessageName] = _Name,
                [NatsMessagingHeaders.ExternalSubject] = _Subject,
            },
            "{}"u8.ToArray()
        );

        // when
        var subject = NatsTransport.ResolveExternalSubject(message, binding);
        var wire = NatsTransport.CreatePublishHeaders(message);

        // then
        subject.Should().Be(_Subject);
        wire.Should().NotContainKey(NatsMessagingHeaders.ExternalSubject);
        wire.Should().ContainKey(Headers.MessageId);
    }

    [Fact]
    public void should_fail_a_send_whose_stored_subject_no_longer_matches_the_filter()
    {
        // given
        var binding = _Binding(MessageLane.Queue, n => n.ExternalSubject(_Filter, s => s.PublishTo(_SharedSelector)));
        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = _Name,
                [NatsMessagingHeaders.ExternalSubject] = "other.t1.runs.r9.result",
            },
            "{}"u8.ToArray()
        );

        // when
        var act = () => NatsTransport.ResolveExternalSubject(message, binding);

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*does not match*");
    }

    #endregion

    #region Consume

    [Fact]
    public async Task should_filter_an_external_binding_on_its_filter_with_a_durable_named_after_the_contract()
    {
        // given
        await using var services = _Services(_Route(_Name, MessageLane.Queue, n => n.ExternalSubject(_Filter)));
        await using var client = _Client(services, MessageLane.Queue);

        // when
        var filters = client.ConsumerFilters([_Name, "orders.placed"]);

        // then
        filters.Should().BeEquivalentTo([(_Name, _Filter), ("orders.placed", "headless.queue.orders.placed")]);
        NatsConsumerClient
            .BuildDurableName("group", _Name, MessageLane.Queue)
            .Should()
            .Be(NatsPhysicalAddress.Durable(MessageLane.Queue, "group", _Name));
    }

    [Fact]
    public void should_subscribe_an_every_instance_client_to_the_external_filter()
    {
        // when
        var subjects = NatsEveryInstanceListener.BuildEveryInstanceSubjects(
            [_Name, "orders.placed"],
            _ => new HashSet<string>(StringComparer.Ordinal),
            name => string.Equals(name, _Name, StringComparison.Ordinal) ? _Filter : null
        );

        // then
        subjects.Should().BeEquivalentTo("headless.bus.orders.placed", _Filter);
    }

    [Fact]
    public async Task should_admit_a_headerless_delivery_on_an_external_raw_binding_with_its_subject_tenant()
    {
        // given
        await using var services = _Services(
            _Route(
                _Name,
                MessageLane.Queue,
                n => n.ExternalSubject(_Filter, s => s.TenantFromSubjectToken(1)),
                raw: true
            )
        );
        await using var client = await _SubscribedClientAsync(services);
        var msg = _JetStreamMsg(_Subject);
        TransportMessage? received = null;
        client.OnMessageCallback = (message, _) =>
        {
            received = message;
            return Task.CompletedTask;
        };

        // when
        await client.DispatchEnvelopeAsync(_Subject, natsHeaders: null, "{}"u8.ToArray(), msg, msg, AbortToken);

        // then
        received.Should().NotBeNull();
        received!.Value.Headers[Headers.MessageName].Should().Be(_Name);
        received.Value.Headers[Headers.TenantId].Should().Be("t1");
        received.Value.Headers[Headers.TransportAddress].Should().Be(_Subject);
        received.Value.Headers[Headers.MessageId].Should().StartWith($"nats:{_Stream}:42:");
    }

    [Fact]
    public async Task should_admit_a_delivery_with_headers_on_a_strict_external_binding()
    {
        // given
        await using var services = _Services(_Route(_Name, MessageLane.Queue, n => n.ExternalSubject(_Filter)));
        await using var client = await _SubscribedClientAsync(services);
        var msg = _JetStreamMsg(_Subject);
        TransportMessage? received = null;
        client.OnMessageCallback = (message, _) =>
        {
            received = message;
            return Task.CompletedTask;
        };
        var headers = new NatsHeaders { [Headers.MessageId] = "m-1", [Headers.MessageName] = _Name };

        // when
        await client.DispatchEnvelopeAsync(_Subject, headers, "{}"u8.ToArray(), msg, msg, AbortToken);

        // then
        received.Should().NotBeNull();
        received!.Value.Headers[Headers.MessageId].Should().Be("m-1");
    }

    [Theory]
    // A strict external binding still needs the Headless headers.
    [InlineData("strict", "none", false)]
    // The subject names the contract, so a header naming another one is malformed.
    [InlineData("raw", "other-name", false)]
    // A header tenant that differs from the subject's tenant.
    [InlineData("raw", "other-tenant", false)]
    // A subject token longer than a tenant may be.
    [InlineData("raw", "none", true)]
    public async Task should_terminate_a_malformed_delivery_on_an_external_binding(
        string mode,
        string headerCase,
        bool overlongTenant
    )
    {
        // given
        await using var services = _Services(
            _Route(
                _Name,
                MessageLane.Queue,
                n => n.ExternalSubject(_Filter, s => s.TenantFromSubjectToken(1)),
                raw: string.Equals(mode, "raw", StringComparison.Ordinal)
            ),
            _Route("orders.placed", MessageLane.Queue, _ => { })
        );
        await using var client = await _SubscribedClientAsync(services);
        var subject = overlongTenant
            ? $"acme.{new string('t', MessageOptions.TenantIdMaxLength + 1)}.runs.r9.result"
            : _Subject;
        var headers = headerCase switch
        {
            "other-name" => new NatsHeaders { [Headers.MessageId] = "m-1", [Headers.MessageName] = "orders.placed" },
            "other-tenant" => new NatsHeaders { [Headers.TenantId] = "t2" },
            _ => null,
        };
        var msg = _JetStreamMsg(subject);
        var callbackInvoked = false;
        client.OnMessageCallback = (_, _) =>
        {
            callbackInvoked = true;
            return Task.CompletedTask;
        };

        // when
        await client.DispatchEnvelopeAsync(subject, headers, "{}"u8.ToArray(), msg, msg, AbortToken);

        // then
        callbackInvoked.Should().BeFalse();
        await msg.Received(1).AckTerminateAsync(Arg.Any<AckOpts?>(), CancellationToken.None);
    }

    [Fact]
    public void should_report_a_missing_tenant_token_as_an_address_error()
    {
        // given
        var streams = new NatsStreamCatalog().Own(_Stream, s => s.Subjects("acme.>"));
        var subjects = NatsExternalSubjects.Create(
            [_Route(_Name, MessageLane.Queue, n => n.ExternalSubject("acme.>", s => s.TenantFromSubjectToken(3)))],
            streams
        );
        var arrivals = NatsArrivalNames.Create(
            MessageLane.Queue,
            [_Name],
            new HashSet<string>(StringComparer.Ordinal),
            [],
            subjects.All
        );

        // when
        var arrival = arrivals.Arrival("NATS", MessageLane.Queue, "acme.t1.runs");
        var unrelated = arrivals.Arrival("NATS", MessageLane.Queue, "other.t1");

        // then
        arrival.ArrivalName.Should().Be(_Name);
        arrival.AddressIsAuthoritative.Should().BeTrue();
        arrival.AddressTenant.Should().BeNull();
        arrival.AddressTenantError.Should().Contain("token 3");
        unrelated.ArrivalName.Should().BeNull();
        unrelated.AddressIsAuthoritative.Should().BeFalse();
    }

    #endregion

    private static ProviderHeaderContribution _Contribution(Action<NatsMessageConfigBuilder<RunResult>> configure) =>
        ((IProviderHeaderContributions)_Config(configure)).HeaderContributions.Single();

    private static object _Config(Action<NatsMessageConfigBuilder<RunResult>> configure)
    {
        var builder = new MessageContractBuilder<RunResult>(_Name, "1");
        builder.OnQueue(q => q.UseNats(configure));
        return builder.Build().Queue.ProviderConfigs.Values.Single();
    }

    private static MessageMetadata _Route(
        string name,
        MessageLane lane,
        Action<NatsMessageConfigBuilder<RunResult>> configure,
        bool raw = false
    )
    {
        var builder = new MessageContractBuilder<RunResult>(name, "1");
        if (lane == MessageLane.Bus)
        {
            builder.OnBus(b => b.UseNats(configure));
        }
        else
        {
            builder.OnQueue(q => q.UseNats(configure));
        }

        var contract = builder.Build();

        return new MessageMetadata(
            new MessageRouteKey(typeof(RunResult), name, lane),
            typeof(RunResult),
            "1",
            CorrelationSelector: null,
            lane == MessageLane.Bus ? contract.Bus.ProviderConfigs : contract.Queue.ProviderConfigs,
            RawPayload: raw
        );
    }

    private static NatsExternalBinding _Binding(MessageLane lane, Action<NatsMessageConfigBuilder<RunResult>> configure)
    {
        var streams = new NatsStreamCatalog().Own(_Stream, s => s.Subjects("acme.>"));
        return NatsExternalSubjects.Create([_Route(_Name, lane, configure)], streams).Find(lane, _Name)!;
    }

    private static ServiceProvider _Services(params MessageMetadata[] routes)
    {
        var options = new NatsMessagingOptions { Servers = "nats://localhost:4222" };
        options.Streams.Own(_Stream, s => s.Subjects("acme.>"));
        var registry = Substitute.For<IMessageMetadataRegistry>();
        registry.GetAll().Returns(routes);

        return new ServiceCollection()
            .AddSingleton(registry)
            .AddSingleton(new TransportEnvelopeGate(routes))
            .AddSingleton(MsOptions.Options.Create(options))
            .AddSingleton(NatsExternalSubjects.Create(routes, options.Streams))
            .BuildServiceProvider();
    }

    private static NatsConsumerClient _Client(IServiceProvider services, MessageLane lane) =>
        new(
            "test-group",
            0,
            services.GetRequiredService<MsOptions.IOptions<NatsMessagingOptions>>(),
            services,
            lane: lane
        )
        {
            OnLogCallback = _ => { },
        };

    private static async Task<NatsConsumerClient> _SubscribedClientAsync(IServiceProvider services)
    {
        var client = _Client(services, MessageLane.Queue);
        await client.SubscribeAsync([_Name, "orders.placed"], AbortToken);
        return client;
    }

    private static INatsJSMsg<ReadOnlyMemory<byte>> _JetStreamMsg(string subject)
    {
        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();
        msg.Subject.Returns(subject);
        msg.ReplyTo.Returns($"$JS.ACK.{_Stream}.consumer.1.42.7.1791633600123456789.0");
        msg.Metadata.Returns(
            new NatsJSMsgMetadata(
                new NatsJSSequencePair(42, 7),
                NumDelivered: 1,
                NumPending: 0,
                DateTimeOffset.FromUnixTimeMilliseconds(1_791_633_600_123),
                _Stream,
                "consumer",
                string.Empty
            )
        );
        return msg;
    }

    public sealed record RunResult(string Tenant, string Run);
}
