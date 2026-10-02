// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.Loader;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>What the consumers compiled by <see cref="GeneratedDispatchTests"/> did, read back by the test.</summary>
public sealed class DispatchProbe
{
    public List<string> Calls { get; } = [];
}

/// <summary>
/// Runs generated dispatchers: compiles a consumer assembly through the generator, loads it, registers its module into a
/// catalog, and invokes the dispatch the catalog received, so the test sees what the emitted code does at run time.
/// </summary>
public sealed class GeneratedDispatchTests : TestBase
{
    private const string _Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Headless.Messaging;

        namespace Billing;

        public sealed record InvoiceIssued(string Number);
        public sealed record InvoicePaid(string Number);

        [BusConsumer("billing.invoice-projection")]
        public sealed class InvoiceProjection(Tests.DispatchProbe probe)
            : IConsume<InvoiceIssued>, IConsume<InvoicePaid>, IConsumerLifecycle, IDisposable
        {
            ValueTask IConsume<InvoiceIssued>.ConsumeAsync(ConsumeContext<InvoiceIssued> context, CancellationToken cancellationToken)
            {
                probe.Calls.Add("issued:" + context.Message.Number);
                return default;
            }

            public ValueTask ConsumeAsync(ConsumeContext<InvoicePaid> context, CancellationToken cancellationToken)
            {
                probe.Calls.Add("paid:" + context.Message.Number);
                return default;
            }

            public ValueTask OnStartingAsync(CancellationToken cancellationToken)
            {
                probe.Calls.Add("starting");
                return default;
            }

            public ValueTask OnStoppingAsync(CancellationToken cancellationToken)
            {
                probe.Calls.Add("stopping");
                throw new InvalidOperationException("A failing stop hook must not mask the delivery.");
            }

            public void Dispose() => probe.Calls.Add("disposed");
        }

        public sealed record PriceChanged(string Number);

        [BusConsumer("billing.price-cache", EveryInstance = true)]
        public sealed class PriceCache(Tests.DispatchProbe probe) : IConsume<PriceChanged>, IOnSubscriptionEstablished, IDisposable
        {
            public ValueTask ConsumeAsync(ConsumeContext<PriceChanged> context, CancellationToken cancellationToken)
            {
                probe.Calls.Add("price:" + context.Message.Number);
                return default;
            }

            public ValueTask OnSubscriptionEstablishedAsync(SubscriptionEstablishedContext context, CancellationToken cancellationToken)
            {
                probe.Calls.Add("established:" + context.Generation);
                return default;
            }

            public void Dispose() => probe.Calls.Add("disposed");
        }

        public sealed record GetQuote(string Number);
        public sealed record Quote(string Number);

        [QueueConsumer("pricing.get-quote")]
        public sealed class GetQuoteResponder(Tests.DispatchProbe probe) : IRespond<GetQuote, Quote>
        {
            async ValueTask<Quote> IRespond<GetQuote, Quote>.RespondAsync(ConsumeContext<GetQuote> context, CancellationToken cancellationToken)
            {
                await Task.Yield();
                probe.Calls.Add("quote:" + context.Message.Number);
                return string.Equals(context.Message.Number, "NONE", StringComparison.Ordinal) ? null! : new Quote("Q-" + context.Message.Number);
            }
        }
        """;

    private const string _InvoiceProjection = "billing.invoice-projection";

    private const string _PriceCache = "billing.price-cache";

    private const string _GetQuote = "pricing.get-quote";

    [Fact]
    public async Task should_invoke_the_consume_overload_that_matches_the_typed_context()
    {
        // given
        var declarations = _RegisterGeneratedModule(out var messageTypes);
        await using var provider = new ServiceCollection().AddSingleton<DispatchProbe>().BuildServiceProvider();
        var probe = provider.GetRequiredService<DispatchProbe>();

        // when
        await declarations[0].Dispatch(provider, _Context(messageTypes["InvoicePaid"], "INV-2"), AbortToken);
        await declarations[0].Dispatch(provider, _Context(messageTypes["InvoiceIssued"], "INV-1"), AbortToken);

        // then
        probe
            .Calls.Should()
            .Equal(
                "starting",
                "paid:INV-2",
                "stopping",
                "disposed",
                "starting",
                "issued:INV-1",
                "stopping",
                "disposed"
            );
    }

    [Fact]
    public async Task should_hand_every_message_of_a_class_its_one_dispatcher_and_reject_other_messages()
    {
        // when
        var declarations = _RegisterGeneratedModule(out _);

        // then
        var projection = declarations
            .Where(x => string.Equals(x.Identity, _InvoiceProjection, StringComparison.Ordinal))
            .ToList();
        projection.Select(x => x.MessageType.Name).Should().Equal("InvoiceIssued", "InvoicePaid");
        projection.Should().OnlyContain(x => x.Lane == MessageLane.Bus);
        projection[0].Dispatch.Should().Be(projection[1].Dispatch);

        await using var provider = new ServiceCollection().AddSingleton<DispatchProbe>().BuildServiceProvider();
        var unrelated = _Context(typeof(UnrelatedMessage), "X-1");
        var dispatch = () => declarations[0].Dispatch(provider, unrelated, AbortToken).AsTask();
        await dispatch
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*InvoiceProjection*UnrelatedMessage*");
    }

    [Fact]
    public async Task should_build_the_consumer_from_its_container_registration_and_leave_it_to_the_container()
    {
        // given - an application registration, or a decorator around it, must not be bypassed
        var declarations = _RegisterGeneratedModule(out var messageTypes);
        var probe = new DispatchProbe();
        var consumerType = declarations[0].ConsumerType;
        var services = new ServiceCollection().AddSingleton(probe);
        services.AddSingleton(
            consumerType,
            sp =>
            {
                probe.Calls.Add("resolved");
                return ActivatorUtilities.CreateInstance(sp, consumerType);
            }
        );
        await using var provider = services.BuildServiceProvider();

        // when
        await declarations[0].Dispatch(provider, _Context(messageTypes["InvoicePaid"], "INV-2"), AbortToken);
        await declarations[0].Dispatch(provider, _Context(messageTypes["InvoicePaid"], "INV-3"), AbortToken);

        // then - one container instance served both deliveries, and the dispatcher never disposed it
        probe
            .Calls.Should()
            .Equal("resolved", "starting", "paid:INV-2", "stopping", "starting", "paid:INV-3", "stopping");
    }

    [Fact]
    public void should_hand_messaging_the_subscription_hook_of_an_every_instance_class_that_implements_it()
    {
        // when
        var declarations = _RegisterGeneratedModule(out _);

        // then
        declarations
            .Single(x => string.Equals(x.Identity, _PriceCache, StringComparison.Ordinal))
            .OnSubscriptionEstablished.Should()
            .NotBeNull();
        declarations
            .Where(x => string.Equals(x.Identity, _InvoiceProjection, StringComparison.Ordinal))
            .Should()
            .OnlyContain(x => x.OnSubscriptionEstablished == null);
    }

    [Fact]
    public async Task should_run_the_subscription_hook_on_an_instance_it_constructs_and_dispose_it()
    {
        // given
        var hook = _RegisterGeneratedModule(out _)
            .Single(x => string.Equals(x.Identity, _PriceCache, StringComparison.Ordinal))
            .OnSubscriptionEstablished!;
        await using var provider = new ServiceCollection().AddSingleton<DispatchProbe>().BuildServiceProvider();

        // when
        await hook(
            provider,
            new SubscriptionEstablishedContext(_PriceCache, ["prices"], IsReconnect: true, 2),
            AbortToken
        );

        // then
        provider.GetRequiredService<DispatchProbe>().Calls.Should().Equal("established:2", "disposed");
    }

    [Fact]
    public async Task should_run_the_subscription_hook_and_the_deliveries_on_the_same_container_registration()
    {
        // given
        var declarations = _RegisterGeneratedModule(out var messageTypes);
        var priceCache = declarations.Single(x => string.Equals(x.Identity, _PriceCache, StringComparison.Ordinal));
        var probe = new DispatchProbe();
        var services = new ServiceCollection().AddSingleton(probe);
        services.AddSingleton(
            priceCache.ConsumerType,
            sp =>
            {
                probe.Calls.Add("resolved");
                return ActivatorUtilities.CreateInstance(sp, priceCache.ConsumerType);
            }
        );
        await using var provider = services.BuildServiceProvider();

        // when
        await priceCache.OnSubscriptionEstablished!(
            provider,
            new SubscriptionEstablishedContext(_PriceCache, ["prices"], IsReconnect: false, 1),
            AbortToken
        );
        await priceCache.Dispatch(provider, _Context(messageTypes["PriceChanged"], "SKU-1"), AbortToken);

        // then
        probe.Calls.Should().Equal("resolved", "established:1", "price:SKU-1");
    }

    [Fact]
    public void should_declare_a_responder_on_the_queue_lane_with_its_response_type()
    {
        // when
        var declarations = _RegisterGeneratedModule(out var messageTypes);

        // then
        var responder = declarations.Single(x => string.Equals(x.Identity, _GetQuote, StringComparison.Ordinal));
        responder.Lane.Should().Be(MessageLane.Queue);
        responder.MessageType.Should().Be(messageTypes["GetQuote"]);
        responder.ResponseType.Should().Be(messageTypes["Quote"]);
        declarations
            .Where(x => !string.Equals(x.Identity, _GetQuote, StringComparison.Ordinal))
            .Should()
            .OnlyContain(x => x.ResponseType == null);
    }

    [Fact]
    public async Task should_record_the_value_respond_async_returned_as_the_reply_and_leave_the_callback_response_empty()
    {
        // given
        var declarations = _RegisterGeneratedModule(out var messageTypes);
        var responder = declarations.Single(x => string.Equals(x.Identity, _GetQuote, StringComparison.Ordinal));
        await using var provider = new ServiceCollection().AddSingleton<DispatchProbe>().BuildServiceProvider();
        var context = _Context(messageTypes["GetQuote"], "7");

        // when
        await responder.Dispatch(provider, context, AbortToken);

        // then
        provider.GetRequiredService<DispatchProbe>().Calls.Should().Equal("quote:7");
        context.HasReply.Should().BeTrue();
        context.ReplyType.Should().Be(messageTypes["Quote"]);
        context.Reply.Should().BeOfType(messageTypes["Quote"]);
        messageTypes["Quote"].GetProperty("Number")!.GetValue(context.Reply).Should().Be("Q-7");
        context.Response.Should().BeNull();
        context.ResponseType.Should().BeNull();
    }

    [Fact]
    public async Task should_record_a_null_result_as_a_reply_so_messaging_can_tell_it_from_no_reply()
    {
        // given
        var declarations = _RegisterGeneratedModule(out var messageTypes);
        var responder = declarations.Single(x => string.Equals(x.Identity, _GetQuote, StringComparison.Ordinal));
        await using var provider = new ServiceCollection().AddSingleton<DispatchProbe>().BuildServiceProvider();
        var context = _Context(messageTypes["GetQuote"], "NONE");

        // when
        await responder.Dispatch(provider, context, AbortToken);

        // then
        context.HasReply.Should().BeTrue();
        context.Reply.Should().BeNull();
        context.ReplyType.Should().Be(messageTypes["Quote"]);
    }

    [Fact]
    public async Task should_record_no_reply_for_a_plain_consumer()
    {
        // given
        var declarations = _RegisterGeneratedModule(out var messageTypes);
        await using var provider = new ServiceCollection().AddSingleton<DispatchProbe>().BuildServiceProvider();
        var context = _Context(messageTypes["InvoicePaid"], "INV-2");

        // when
        await declarations[0].Dispatch(provider, context, AbortToken);

        // then
        context.HasReply.Should().BeFalse();
        context.Reply.Should().BeNull();
        context.ReplyType.Should().BeNull();
    }

    public sealed record UnrelatedMessage(string Number);

    private static IReadOnlyList<MessagingConsumerDeclaration> _RegisterGeneratedModule(
        out Dictionary<string, Type> messageTypes
    )
    {
        GeneratorTestHelper.Run(_Source, out var diagnostics, out var compilation);
        diagnostics.Should().NotContain(x => x.Severity == DiagnosticSeverity.Error);

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join(Environment.NewLine, emit.Diagnostics));
        stream.Position = 0;

        // A collectible context keeps each compiled copy of the source out of the test process's default context.
        var assembly = new AssemblyLoadContext(name: null, isCollectible: true).LoadFromStream(stream);
        messageTypes = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["InvoiceIssued"] = assembly.GetType("Billing.InvoiceIssued", throwOnError: true)!,
            ["InvoicePaid"] = assembly.GetType("Billing.InvoicePaid", throwOnError: true)!,
            ["PriceChanged"] = assembly.GetType("Billing.PriceChanged", throwOnError: true)!,
            ["GetQuote"] = assembly.GetType("Billing.GetQuote", throwOnError: true)!,
            ["Quote"] = assembly.GetType("Billing.Quote", throwOnError: true)!,
        };

        var module = assembly.GetType($"{GeneratorTestHelper.AssemblyName}.MessagingModule", throwOnError: true)!;
        var register = module.GetInterfaceMap(typeof(IMessagingModule)).TargetMethods.Single();
        var catalog = new MessagingCatalogBuilder();
        catalog.AddModule(module, builder => register.Invoke(null, [builder]));

        return catalog.Consumers;
    }

    /// <summary>Builds the typed context the runtime would hand the dispatcher for one message.</summary>
    private static ConsumeContext _Context(Type messageType, string number)
    {
        var contextType = typeof(ConsumeContext<>).MakeGenericType(messageType);
        var context = (ConsumeContext)Activator.CreateInstance(contextType)!;
        contextType
            .GetProperty(
                nameof(ConsumeContext<>.Message),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )!
            .SetValue(context, Activator.CreateInstance(messageType, number));
        return context;
    }
}
