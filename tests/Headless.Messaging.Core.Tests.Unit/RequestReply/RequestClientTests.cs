// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.RequestReply;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Tests.RequestReply;

/// <summary>
/// Drives <see cref="IRequestClient"/> through a real in-memory host. The responder is simulated at the transport: it
/// sees each request exactly as it leaves the caller and answers through the caller's real reply channel.
/// </summary>
[Collection(RequestReplyCollection.Name)]
public sealed class RequestClientTests : TestBase
{
    private static readonly TimeSpan _Timeout = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeResponder _responder = new();

    [Fact]
    public async Task should_complete_the_call_with_the_deserialized_response_when_a_matching_reply_arrives()
    {
        // given
        await using var provider = await _StartHostAsync();
        _responder.OnRequest = request => new ValueTask(Replies.SendOkAsync(provider, request, new PriceQuote(42m)));

        // when
        var quote = await _Client(provider)
            .RequestAsync<PriceQuoteRequest, PriceQuote>(new PriceQuoteRequest("sku-1"), cancellationToken: AbortToken);

        // then
        quote.Should().Be(new PriceQuote(42m));
        var request = _responder.Sent.Should().ContainSingle().Subject;
        request.Headers[Headers.RequestId].Should().NotBeNullOrWhiteSpace();
        request.Headers[Headers.ReplyTo].Should().StartWith("headless.reply.");
        request.Headers[Headers.Intent].Should().Be("Queue");
        request.Headers[Headers.ResolvedDeliveryMode].Should().Be("Direct");
        _Pending(provider).TrackedCount.Should().Be(1, "the completed call stays as a tombstone for duplicates");
    }

    [Fact]
    public async Task should_stamp_a_deadline_one_timeout_after_the_call_started()
    {
        // given
        await using var provider = await _StartHostAsync();
        var startedAt = _time.GetUtcNow();

        // when
        var call = _Client(provider)
            .RequestAsync<PriceQuoteRequest, PriceQuote>(
                new PriceQuoteRequest("sku-1"),
                new RequestOptions { Timeout = TimeSpan.FromSeconds(7) },
                AbortToken
            );
        var request = await _responder.NextRequestAsync(AbortToken);
        _time.Advance(TimeSpan.FromSeconds(7));
        await call.Awaiting(x => x).Should().ThrowAsync<RequestTimeoutException>();

        // then
        DateTimeOffset
            .Parse(request.Headers[Headers.RequestDeadline]!, CultureInfo.InvariantCulture)
            .Should()
            .Be(startedAt.AddSeconds(7));
    }

    [Fact]
    public async Task should_time_out_remove_the_pending_call_and_count_a_later_reply_as_late()
    {
        // given
        await using var provider = await _StartHostAsync();
        using var measurements = new RequestReplyMeasurements();
        var call = _Client(provider)
            .RequestAsync<PriceQuoteRequest, PriceQuote>(
                new PriceQuoteRequest("sku-1"),
                new RequestOptions { Timeout = _Timeout },
                AbortToken
            );
        var request = await _responder.NextRequestAsync(AbortToken);

        // when
        _time.Advance(_Timeout);

        // then
        var thrown = await call.Awaiting(x => x).Should().ThrowAsync<RequestTimeoutException>();
        thrown.Which.RequestId.Should().Be(request.Headers[Headers.RequestId]);
        thrown.Which.Timeout.Should().Be(_Timeout);
        _Pending(provider).PendingCount.Should().Be(0);

        await Replies.SendOkAsync(provider, request, new PriceQuote(1m));
        await measurements.WaitForDropAsync("late", AbortToken);
    }

    [Fact]
    public async Task should_forget_an_ended_call_after_one_more_timeout()
    {
        // given
        await using var provider = await _StartHostAsync();
        var call = _Client(provider)
            .RequestAsync<PriceQuoteRequest, PriceQuote>(
                new PriceQuoteRequest("sku-1"),
                new RequestOptions { Timeout = _Timeout },
                AbortToken
            );
        await _responder.NextRequestAsync(AbortToken);
        _time.Advance(_Timeout);
        await call.Awaiting(x => x).Should().ThrowAsync<RequestTimeoutException>();

        // when
        _time.Advance(_Timeout);

        // then
        _Pending(provider).TrackedCount.Should().Be(0);
    }

    [Fact]
    public async Task should_throw_operation_canceled_and_remove_the_pending_call_when_the_caller_cancels()
    {
        // given
        await using var provider = await _StartHostAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var call = _Client(provider)
            .RequestAsync<PriceQuoteRequest, PriceQuote>(
                new PriceQuoteRequest("sku-1"),
                cancellationToken: cancellation.Token
            );
        await _responder.NextRequestAsync(AbortToken);

        // when
        await cancellation.CancelAsync();

        // then
        await call.Awaiting(x => x).Should().ThrowAsync<OperationCanceledException>();
        _Pending(provider).PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task should_throw_not_sent_at_once_and_leave_no_timer_when_publish_middleware_suppresses_the_request()
    {
        // given
        await using var provider = await _StartHostAsync(messaging =>
            messaging.AddPublishMiddlewareFor<SuppressingMiddleware, PriceQuoteRequest>(MessageLane.Queue)
        );

        // when
        var act = () =>
            _Client(provider)
                .RequestAsync<PriceQuoteRequest, PriceQuote>(
                    new PriceQuoteRequest("sku-1"),
                    cancellationToken: AbortToken
                );

        // then
        await act.Should().ThrowAsync<RequestNotSentException>();
        _responder.Sent.Should().BeEmpty();
        _Pending(provider).TrackedCount.Should().Be(0);
    }

    [Fact]
    public async Task should_surface_the_send_failure_and_remove_the_pending_call_when_the_transport_throws()
    {
        // given
        await using var provider = await _StartHostAsync();
        _responder.FailWith = new InvalidOperationException("broker unreachable");

        // when
        var act = () =>
            _Client(provider)
                .RequestAsync<PriceQuoteRequest, PriceQuote>(
                    new PriceQuoteRequest("sku-1"),
                    cancellationToken: AbortToken
                );

        // then
        await act.Should().ThrowAsync<PublisherSentFailedException>();
        _Pending(provider).TrackedCount.Should().Be(0);
    }

    [Fact]
    public async Task should_drop_a_reply_from_another_tenant_and_time_out()
    {
        // given
        await using var provider = await _StartHostAsync();
        using var measurements = new RequestReplyMeasurements();
        var call = _Client(provider)
            .RequestAsync<PriceQuoteRequest, PriceQuote>(
                new PriceQuoteRequest("sku-1"),
                new RequestOptions { Timeout = _Timeout, TenantId = "tenant-a" },
                AbortToken
            );
        var request = await _responder.NextRequestAsync(AbortToken);
        request.Headers[Headers.TenantId].Should().Be("tenant-a");

        // when
        await Replies.SendOkAsync(provider, request, new PriceQuote(1m), h => h[Headers.TenantId] = "tenant-b");
        await measurements.WaitForDropAsync("tenant_mismatch", AbortToken);
        _time.Advance(_Timeout);

        // then
        await call.Awaiting(x => x).Should().ThrowAsync<RequestTimeoutException>();
    }

    [Theory]
    [InlineData("other.contract", null)]
    [InlineData(null, "2")]
    public async Task should_fail_with_a_contract_mismatch_when_the_reply_carries_another_response_contract(
        string? name,
        string? version
    )
    {
        // given
        await using var provider = await _StartHostAsync();
        _responder.OnRequest = request => new ValueTask(
            Replies.SendOkAsync(
                provider,
                request,
                new PriceQuote(1m),
                h =>
                {
                    h[Headers.MessageName] = name ?? h[Headers.MessageName];
                    h[Headers.ContractVersion] = version ?? h[Headers.ContractVersion];
                }
            )
        );

        // when
        var act = () =>
            _Client(provider)
                .RequestAsync<PriceQuoteRequest, PriceQuote>(
                    new PriceQuoteRequest("sku-1"),
                    cancellationToken: AbortToken
                );

        // then
        var thrown = await act.Should().ThrowAsync<ResponseContractMismatchException>();
        thrown.Which.ExpectedMessageName.Should().Be(nameof(PriceQuote));
        thrown.Which.ExpectedContractVersion.Should().Be("1");
        thrown.Which.ActualMessageName.Should().Be(name ?? nameof(PriceQuote));
        thrown.Which.ActualContractVersion.Should().Be(version ?? "1");
    }

    [Fact]
    public async Task should_expect_the_response_contract_declared_for_the_response_type()
    {
        // given
        await using var provider = await _StartHostAsync(configureServices: services =>
            services.AddMessageContract<PriceQuote>("pricing.quote", "3")
        );
        _responder.OnRequest = request => new ValueTask(Replies.SendOkAsync(provider, request, new PriceQuote(9m)));

        // when
        var quote = await _Client(provider)
            .RequestAsync<PriceQuoteRequest, PriceQuote>(new PriceQuoteRequest("sku-1"), cancellationToken: AbortToken);

        // then
        quote.Should().Be(new PriceQuote(9m));
        provider
            .GetRequiredService<IMessagePublishRequestFactory>()
            .ResolveContract(typeof(PriceQuote), MessageLane.Queue)
            .Should()
            .Be(("pricing.quote", "3"));
    }

    [Fact]
    public async Task should_fail_with_the_fault_code_when_the_responder_answers_with_a_fault()
    {
        // given
        await using var provider = await _StartHostAsync();
        _responder.OnRequest = request => new ValueTask(
            Replies.SendFaultAsync(
                provider,
                request,
                """{"code":"handler_failed","exceptionType":"System.TimeoutException","detail":"db slow"}"""
            )
        );

        // when
        var act = () =>
            _Client(provider)
                .RequestAsync<PriceQuoteRequest, PriceQuote>(
                    new PriceQuoteRequest("sku-1"),
                    cancellationToken: AbortToken
                );

        // then
        var thrown = await act.Should().ThrowAsync<RequestFaultedException>();
        thrown.Which.Code.Should().Be(RequestFaultCodes.HandlerFailed);
        thrown.Which.RemoteExceptionType.Should().Be("System.TimeoutException");
        thrown.Which.Detail.Should().Be("db slow");
    }

    [Fact]
    public async Task should_wait_for_the_reply_listener_and_succeed_when_called_before_the_host_is_ready()
    {
        // given: a host that has not started messaging yet
        await using var provider = _BuildHost();
        _responder.OnRequest = request => new ValueTask(Replies.SendOkAsync(provider, request, new PriceQuote(5m)));
        var call = _Client(provider)
            .RequestAsync<PriceQuoteRequest, PriceQuote>(new PriceQuoteRequest("sku-1"), cancellationToken: AbortToken);
        call.IsCompleted.Should().BeFalse();

        // when
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        // then
        (await call)
            .Should()
            .Be(new PriceQuote(5m));
    }

    [Fact]
    public async Task should_throw_not_sent_when_the_listener_is_not_ready_within_the_timeout()
    {
        // given: a host that never starts messaging
        await using var provider = _BuildHost();
        var call = _Client(provider)
            .RequestAsync<PriceQuoteRequest, PriceQuote>(
                new PriceQuoteRequest("sku-1"),
                new RequestOptions { Timeout = _Timeout },
                AbortToken
            );

        // when
        _time.Advance(_Timeout);

        // then
        await call.Awaiting(x => x).Should().ThrowAsync<RequestNotSentException>();
        _responder.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task should_abort_every_pending_call_and_refuse_new_ones_when_the_host_stops()
    {
        // given
        await using var provider = await _StartHostAsync();
        var client = _Client(provider);
        var first = client.RequestAsync<PriceQuoteRequest, PriceQuote>(
            new PriceQuoteRequest("sku-1"),
            cancellationToken: AbortToken
        );
        var second = client.RequestAsync<PriceQuoteRequest, PriceQuote>(
            new PriceQuoteRequest("sku-2"),
            cancellationToken: AbortToken
        );
        await _responder.NextRequestAsync(AbortToken);
        await _responder.NextRequestAsync(AbortToken);

        // when
        await provider.GetRequiredService<Bootstrapper>().StopAsync(AbortToken);

        // then
        await first.Awaiting(x => x).Should().ThrowExactlyAsync<RequestAbortedException>();
        await second.Awaiting(x => x).Should().ThrowExactlyAsync<RequestAbortedException>();
        var late = () =>
            client.RequestAsync<PriceQuoteRequest, PriceQuote>(
                new PriceQuoteRequest("sku-3"),
                cancellationToken: AbortToken
            );
        await late.Should().ThrowAsync<RequestNotSentException>();
        _responder.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_keep_the_framework_message_id_and_request_headers_when_middleware_rewrites_the_message_id()
    {
        // given
        await using var provider = await _StartHostAsync(messaging =>
            messaging.AddPublishMiddlewareFor<MessageIdRewritingMiddleware, PriceQuoteRequest>(MessageLane.Queue)
        );
        _responder.OnRequest = request => new ValueTask(Replies.SendOkAsync(provider, request, new PriceQuote(3m)));

        // when
        var quote = await _Client(provider)
            .RequestAsync<PriceQuoteRequest, PriceQuote>(new PriceQuoteRequest("sku-1"), cancellationToken: AbortToken);

        // then
        quote.Should().Be(new PriceQuote(3m));
        var request = _responder.Sent.Should().ContainSingle().Subject;
        request.Headers[Headers.MessageId].Should().NotBe(MessageIdRewritingMiddleware.ForgedMessageId);
        request.Headers[Headers.RequestId].Should().NotBe(MessageIdRewritingMiddleware.ForgedMessageId);
        request.Headers[Headers.ReplyTo].Should().StartWith("headless.reply.");
    }

    [Fact]
    public async Task should_refuse_to_send_when_middleware_tries_to_set_the_reply_address()
    {
        // given
        await using var provider = await _StartHostAsync(messaging =>
            messaging.AddPublishMiddlewareFor<ReplyAddressForgingMiddleware, PriceQuoteRequest>(MessageLane.Queue)
        );

        // when
        var act = () =>
            _Client(provider)
                .RequestAsync<PriceQuoteRequest, PriceQuote>(
                    new PriceQuoteRequest("sku-1"),
                    cancellationToken: AbortToken
                );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*'{Headers.ReplyTo}' is reserved*");
        _responder.Sent.Should().BeEmpty();
        _Pending(provider).TrackedCount.Should().Be(0);
    }

    [Fact]
    public async Task should_reject_a_reserved_request_header_in_the_call_options()
    {
        // given
        await using var provider = await _StartHostAsync();
        var options = new RequestOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.RequestId] = "forged" },
        };

        // when
        var act = () =>
            _Client(provider)
                .RequestAsync<PriceQuoteRequest, PriceQuote>(new PriceQuoteRequest("sku-1"), options, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>();
        _responder.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task should_stamp_the_ambient_tenant_and_round_trip_it_when_the_host_propagates_tenants()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant()));
        await using var provider = await _StartHostAsync(services: builder.Services);
        _responder.OnRequest = request => new ValueTask(Replies.SendOkAsync(provider, request, new PriceQuote(7m)));
        var currentTenant = provider.GetRequiredService<ICurrentTenant>();

        // when
        PriceQuote quote;
        using (currentTenant.Change("tenant-a"))
        {
            quote = await _Client(provider)
                .RequestAsync<PriceQuoteRequest, PriceQuote>(
                    new PriceQuoteRequest("sku-1"),
                    cancellationToken: AbortToken
                );
        }

        // then
        quote.Should().Be(new PriceQuote(7m));
        _responder.Sent.Should().ContainSingle().Which.Headers[Headers.TenantId].Should().Be("tenant-a");
    }

    [Fact]
    public async Task should_not_stamp_the_ambient_tenant_when_the_host_does_not_propagate_tenants()
    {
        // given
        await using var provider = await _StartHostAsync();
        _responder.OnRequest = request => new ValueTask(Replies.SendOkAsync(provider, request, new PriceQuote(7m)));
        var currentTenant = provider.GetRequiredService<ICurrentTenant>();

        // when
        using (currentTenant.Change("tenant-a"))
        {
            await _Client(provider)
                .RequestAsync<PriceQuoteRequest, PriceQuote>(
                    new PriceQuoteRequest("sku-1"),
                    cancellationToken: AbortToken
                );
        }

        // then
        _responder.Sent.Should().ContainSingle().Which.Headers.Should().NotContainKey(Headers.TenantId);
    }

    [Fact]
    public async Task should_inject_the_request_span_trace_context_into_the_request()
    {
        // given: the application's OpenTelemetry setup normally assigns the W3C propagator
        Sdk.SetDefaultTextMapPropagator(
            new CompositeTextMapPropagator([new TraceContextPropagator(), new BaggagePropagator()])
        );
        await using var provider = await _StartHostAsync();
        _responder.OnRequest = request => new ValueTask(Replies.SendOkAsync(provider, request, new PriceQuote(1m)));
        var publishSpans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source =>
                string.Equals(source.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal),
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (publishSpans)
                {
                    publishSpans.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        // when
        await _Client(provider)
            .RequestAsync<PriceQuoteRequest, PriceQuote>(new PriceQuoteRequest("sku-1"), cancellationToken: AbortToken);

        // then
        var traceParent = _responder.Sent.Should().ContainSingle().Subject.Headers[Headers.TraceParent];
        traceParent.Should().NotBeNullOrWhiteSpace();
        lock (publishSpans)
        {
            publishSpans.Should().Contain(span => traceParent!.Contains(span.SpanId.ToHexString()));
        }
    }

    [Fact]
    public async Task should_register_no_client_and_open_no_reply_listener_without_add_request_reply()
    {
        // given
        await using var provider = await _StartHostAsync(requests: false);

        // then
        provider.GetService<IRequestClient>().Should().BeNull();
        provider.GetService<ReplyListenerHost>().Should().BeNull();
        provider.GetService<PendingRequests>().Should().BeNull();
    }

    [Fact]
    public async Task should_throw_when_called_inside_a_transactional_inbox_unit()
    {
        // given
        await using var provider = await _StartHostAsync();
        var accessor = provider.GetRequiredService<IConsumeContextAccessor>();
        accessor.Current = _ConsumeContext(Substitute.For<IUnitOfWork>());

        // when
        var act = () =>
            _Client(provider)
                .RequestAsync<PriceQuoteRequest, PriceQuote>(
                    new PriceQuoteRequest("sku-1"),
                    cancellationToken: AbortToken
                );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*transactional inbox unit*");
        _responder.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task should_send_from_inside_a_consumer_on_the_non_transactional_tier()
    {
        // given
        await using var provider = await _StartHostAsync();
        _responder.OnRequest = request => new ValueTask(Replies.SendOkAsync(provider, request, new PriceQuote(2m)));
        var accessor = provider.GetRequiredService<IConsumeContextAccessor>();
        accessor.Current = _ConsumeContext(unitOfWork: null);

        // when
        var quote = await _Client(provider)
            .RequestAsync<PriceQuoteRequest, PriceQuote>(new PriceQuoteRequest("sku-1"), cancellationToken: AbortToken);

        // then
        quote.Should().Be(new PriceQuote(2m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(601)]
    public void should_reject_a_default_timeout_outside_the_allowed_range_at_startup(int seconds)
    {
        // given
        using var provider = _BuildHost(requests => requests.DefaultTimeout = TimeSpan.FromSeconds(seconds));

        // when
        var act = () => provider.GetRequiredService<IOptions<MessagingOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>().WithMessage("*RequestReply.DefaultTimeout*");
    }

    [Fact]
    public async Task should_use_the_configured_default_timeout()
    {
        // given
        await using var provider = await _StartHostAsync(configureRequests: requests =>
            requests.DefaultTimeout = TimeSpan.FromMinutes(10)
        );
        var call = _Client(provider)
            .RequestAsync<PriceQuoteRequest, PriceQuote>(new PriceQuoteRequest("sku-1"), cancellationToken: AbortToken);
        await _responder.NextRequestAsync(AbortToken);

        // when
        _time.Advance(TimeSpan.FromMinutes(10));

        // then
        var thrown = await call.Awaiting(x => x).Should().ThrowAsync<RequestTimeoutException>();
        thrown.Which.Timeout.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task should_record_one_outcome_per_call_without_identifier_tags()
    {
        // given
        await using var provider = await _StartHostAsync();
        using var measurements = new RequestReplyMeasurements();
        var client = _Client(provider);
        _responder.OnRequest = request => new ValueTask(Replies.SendOkAsync(provider, request, new PriceQuote(1m)));

        // when
        await client.RequestAsync<PriceQuoteRequest, PriceQuote>(
            new PriceQuoteRequest("sku-1"),
            cancellationToken: AbortToken
        );
        _responder.OnRequest = null;
        var unanswered = client.RequestAsync<PriceQuoteRequest, PriceQuote>(
            new PriceQuoteRequest("sku-2"),
            new RequestOptions { Timeout = _Timeout },
            AbortToken
        );
        await _responder.NextRequestAsync(AbortToken);
        await _responder.NextRequestAsync(AbortToken);
        _time.Advance(_Timeout);
        await unanswered.Awaiting(x => x).Should().ThrowAsync<RequestTimeoutException>();

        // then
        measurements.OutcomeValues.Should().Equal("replied", "timed_out");
        measurements.Durations.Should().HaveCount(2);
        measurements
            .Outcomes.Concat(measurements.Durations)
            .SelectMany(static tags => tags)
            .Select(static tag => tag.Key)
            .Should()
            .OnlyContain(static key => key == "messaging.request_reply.outcome");
    }

    private ServiceProvider _BuildHost(
        Action<RequestReplyOptions>? configureRequests = null,
        Action<MessagingBuilder>? configureMessaging = null,
        Action<IServiceCollection>? configureServices = null,
        IServiceCollection? services = null,
        bool requests = true
    )
    {
        services ??= new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        configureServices?.Invoke(services);

        var messaging = services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();

            if (requests)
            {
                setup.AddRequestReply(configureRequests);
            }
        });
        configureMessaging?.Invoke(messaging);

        // Registered last, so every Queue send reaches the simulated responder instead of the in-memory broker.
        services.AddSingleton<IQueueTransport>(_responder);

        return services.BuildServiceProvider();
    }

    private async Task<ServiceProvider> _StartHostAsync(
        Action<MessagingBuilder>? configureMessaging = null,
        Action<IServiceCollection>? configureServices = null,
        Action<RequestReplyOptions>? configureRequests = null,
        IServiceCollection? services = null,
        bool requests = true
    )
    {
        var provider = _BuildHost(configureRequests, configureMessaging, configureServices, services, requests);
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        return provider;
    }

    private static IRequestClient _Client(IServiceProvider provider) => provider.GetRequiredService<IRequestClient>();

    private static PendingRequests _Pending(IServiceProvider provider) =>
        provider.GetRequiredService<PendingRequests>();

    private static ConsumeContext<PriceQuoteRequest> _ConsumeContext(IUnitOfWork? unitOfWork)
    {
        return new ConsumeContext<PriceQuoteRequest>
        {
            Lane = MessageLane.Queue,
            Message = new PriceQuoteRequest("inbound"),
            MessageId = "inbound-1",
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UnixEpoch,
            MessageName = "pricing.inbound",
            UnitOfWork = unitOfWork,
        };
    }

    private sealed class SuppressingMiddleware : IPublishMiddleware<PublishContext<PriceQuoteRequest>>
    {
        public ValueTask InvokeAsync(PublishContext<PriceQuoteRequest> context, Func<ValueTask> next)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MessageIdRewritingMiddleware : IPublishMiddleware<PublishContext<PriceQuoteRequest>>
    {
        public const string ForgedMessageId = "forged-message-id";

        public async ValueTask InvokeAsync(PublishContext<PriceQuoteRequest> context, Func<ValueTask> next)
        {
            context.WithOptions(((QueueOptions)context.Options!) with { MessageId = ForgedMessageId });
            await next();
        }
    }

    private sealed class ReplyAddressForgingMiddleware : IPublishMiddleware<PublishContext<PriceQuoteRequest>>
    {
        public async ValueTask InvokeAsync(PublishContext<PriceQuoteRequest> context, Func<ValueTask> next)
        {
            context.WithOptions(
                ((QueueOptions)context.Options!) with
                {
                    Headers = new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        [Headers.ReplyTo] = "headless.reply.attacker",
                    },
                }
            );
            await next();
        }
    }
}
