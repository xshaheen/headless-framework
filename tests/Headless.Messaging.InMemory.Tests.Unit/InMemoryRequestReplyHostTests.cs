// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.InMemory;
using Headless.Messaging.Registration;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

/// <summary>
/// A caller host and a responder host on one shared in-memory transport stand in for two services: the caller awaits
/// the one outcome of each request through its own reply channel, and the responder answers with a response or a typed
/// fault, or not at all once the caller has stopped waiting.
/// </summary>
public sealed class InMemoryRequestReplyHostTests : TestBase
{
    private static readonly RequestOptions _Patient = new() { Timeout = TimeSpan.FromSeconds(10) };

    [Fact]
    public async Task should_return_the_responder_response_to_the_caller()
    {
        // given
        var transport = new MemoryQueue(NullLogger<MemoryQueue>.Instance);
        await using var responder = await _StartResponderAsync(transport);
        await using var caller = await _StartCallerAsync(transport);

        // when
        var quote = await caller
            .GetRequiredService<IRequestClient>()
            .RequestAsync<GetQuote, Quote>(new GetQuote("sku-1"), _Patient, AbortToken);

        // then
        quote.Should().Be(new Quote("sku-1", 42m));
        responder.GetRequiredService<ResponderProbe>().Handled.Should().Equal("quote:sku-1");
    }

    [Fact]
    public async Task should_fail_the_call_with_handler_failed_when_the_responder_throws()
    {
        // given
        var transport = new MemoryQueue(NullLogger<MemoryQueue>.Instance);
        await using var responder = await _StartResponderAsync(transport);
        await using var caller = await _StartCallerAsync(transport);

        // when
        var act = () =>
            caller
                .GetRequiredService<IRequestClient>()
                .RequestAsync<GetBrokenQuote, Quote>(new GetBrokenQuote("sku-1"), _Patient, AbortToken);

        // then — the default classifier treats ArgumentException as permanent, so the fault follows one attempt
        var fault = (await act.Should().ThrowAsync<RequestFaultedException>()).Which;
        fault.Code.Should().Be(RequestFaultCodes.HandlerFailed);
        fault.RemoteExceptionType.Should().BeNull("the responder host did not opt in to exception details");
        responder.GetRequiredService<ResponderProbe>().Handled.Should().Equal("broken:sku-1");
    }

    [Fact]
    public async Task should_fail_the_call_with_no_responder_without_running_a_plain_consumer()
    {
        // given — the request's only consumer implements IConsume<T>, not IRespond<T, TResponse>
        var transport = new MemoryQueue(NullLogger<MemoryQueue>.Instance);
        await using var responder = await _StartResponderAsync(transport);
        await using var caller = await _StartCallerAsync(transport);

        // when
        var act = () =>
            caller
                .GetRequiredService<IRequestClient>()
                .RequestAsync<GetStock, Quote>(new GetStock("sku-1"), _Patient, AbortToken);

        // then
        var fault = (await act.Should().ThrowAsync<RequestFaultedException>()).Which;
        fault.Code.Should().Be(RequestFaultCodes.NoResponder);
        responder.GetRequiredService<ResponderProbe>().Handled.Should().BeEmpty();
    }

    [Fact]
    public async Task should_fail_the_call_with_request_rejected_when_the_responder_rejects_the_contract_version()
    {
        // given — the caller sends version 2 of a contract the responder reads only at version 1
        var transport = new MemoryQueue(NullLogger<MemoryQueue>.Instance);
        await using var responder = await _StartResponderAsync(transport);
        await using var caller = await _StartCallerAsync(transport, legacyQuoteVersion: "2");

        // when
        var act = () =>
            caller
                .GetRequiredService<IRequestClient>()
                .RequestAsync<GetLegacyQuote, Quote>(new GetLegacyQuote("sku-1"), _Patient, AbortToken);

        // then
        var fault = (await act.Should().ThrowAsync<RequestFaultedException>()).Which;
        fault.Code.Should().Be(RequestFaultCodes.RequestRejected);
        responder.GetRequiredService<ResponderProbe>().Handled.Should().BeEmpty();
    }

    [Fact]
    public async Task should_time_out_without_a_reply_when_the_request_arrives_after_its_deadline()
    {
        // given — the responder's clock runs an hour ahead, so every request it receives has already expired
        var transport = new MemoryQueue(NullLogger<MemoryQueue>.Instance);
        await using var responder = await _StartResponderAsync(
            transport,
            new SkewedTimeProvider(TimeSpan.FromHours(1))
        );
        await using var caller = await _StartCallerAsync(transport);

        // when
        var act = () =>
            caller
                .GetRequiredService<IRequestClient>()
                .RequestAsync<GetQuote, Quote>(
                    new GetQuote("sku-1"),
                    new RequestOptions { Timeout = TimeSpan.FromSeconds(1) },
                    AbortToken
                );

        // then — the responder drops it unanswered and never runs the handler; only the caller's timer ends the call
        await act.Should().ThrowAsync<RequestTimeoutException>();
        responder.GetRequiredService<ResponderProbe>().Handled.Should().BeEmpty();
    }

    private static async Task<ServiceProvider> _StartResponderAsync(
        MemoryQueue transport,
        TimeProvider? timeProvider = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ResponderProbe>();
        services.ConfigureMessaging(messaging =>
        {
            _NameContracts(messaging, legacyQuoteVersion: "1");
            messaging.AddModule<PricingModule>();
        });
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
        });

        // Registered last, so the host resolves the shared transport and, when given, the skewed clock.
        services.AddSingleton(transport);
        if (timeProvider is not null)
        {
            services.AddSingleton(timeProvider);
        }

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        return provider;
    }

    private static async Task<ServiceProvider> _StartCallerAsync(MemoryQueue transport, string legacyQuoteVersion = "1")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureMessaging(messaging => _NameContracts(messaging, legacyQuoteVersion));
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
            setup.AddRequestReply();
        });
        services.AddSingleton(transport);

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        return provider;
    }

    private static void _NameContracts(MessagingContributionBuilder messaging, string legacyQuoteVersion)
    {
        messaging.Message<GetQuote>("pricing.get-quote");
        messaging.Message<GetBrokenQuote>("pricing.get-broken-quote");
        messaging.Message<GetLegacyQuote>("pricing.get-legacy-quote", legacyQuoteVersion);
        messaging.Message<GetStock>("pricing.get-stock");
        messaging.Message<Quote>("pricing.quote");
    }

    /// <summary>The system clock moved by a fixed skew, as on a host whose clock drifted.</summary>
    private sealed class SkewedTimeProvider(TimeSpan skew) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return base.GetUtcNow() + skew;
        }
    }
}

public sealed record GetQuote(string Sku);

public sealed record GetBrokenQuote(string Sku);

public sealed record GetLegacyQuote(string Sku);

public sealed record GetStock(string Sku);

public sealed record Quote(string Sku, decimal Price);

public sealed class ResponderProbe
{
    public ConcurrentQueue<string> Handled { get; } = new();
}

public sealed class QuoteDesk(ResponderProbe probe)
    : IRespond<GetQuote, Quote>,
        IRespond<GetBrokenQuote, Quote>,
        IRespond<GetLegacyQuote, Quote>
{
    public ValueTask<Quote> RespondAsync(ConsumeContext<GetQuote> context, CancellationToken cancellationToken)
    {
        probe.Handled.Enqueue("quote:" + context.Message.Sku);
        return ValueTask.FromResult(new Quote(context.Message.Sku, 42m));
    }

    public ValueTask<Quote> RespondAsync(ConsumeContext<GetBrokenQuote> context, CancellationToken cancellationToken)
    {
        probe.Handled.Enqueue("broken:" + context.Message.Sku);
        throw new ArgumentException("The pricing table has no row for this SKU.", nameof(context));
    }

    public ValueTask<Quote> RespondAsync(ConsumeContext<GetLegacyQuote> context, CancellationToken cancellationToken)
    {
        probe.Handled.Enqueue("legacy:" + context.Message.Sku);
        return ValueTask.FromResult(new Quote(context.Message.Sku, 1m));
    }
}

public sealed class StockLog(ResponderProbe probe) : IConsume<GetStock>
{
    public ValueTask ConsumeAsync(ConsumeContext<GetStock> context, CancellationToken cancellationToken)
    {
        probe.Handled.Enqueue("stock:" + context.Message.Sku);
        return ValueTask.CompletedTask;
    }
}

/// <summary>What the source generator emits for the responders above, written out for a test assembly without it.</summary>
public sealed class PricingModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog)
    {
        catalog.AddQueueResponder<QuoteDesk, GetQuote, Quote>("pricing.quote-desk", _Respond<GetQuote>());
        catalog.AddQueueResponder<QuoteDesk, GetBrokenQuote, Quote>("pricing.quote-desk", _Respond<GetBrokenQuote>());
        catalog.AddQueueResponder<QuoteDesk, GetLegacyQuote, Quote>("pricing.quote-desk", _Respond<GetLegacyQuote>());
        catalog.AddQueueConsumer<StockLog, GetStock>(
            "pricing.stock-log",
            static (services, context, cancellationToken) =>
                ActivatorUtilities
                    .CreateInstance<StockLog>(services)
                    .ConsumeAsync((ConsumeContext<GetStock>)context, cancellationToken)
        );
    }

    private static MessageConsumerDispatch _Respond<TRequest>()
        where TRequest : class =>
        static async (services, context, cancellationToken) =>
        {
            var responder = (IRespond<TRequest, Quote>)(object)ActivatorUtilities.CreateInstance<QuoteDesk>(services);
            var typed = (ConsumeContext<TRequest>)context;
            typed.RecordReply(await responder.RespondAsync(typed, cancellationToken).ConfigureAwait(false));
        };
}
