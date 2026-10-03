// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Tests;

/// <summary>
/// Golden snapshots of the complete generated registration source, one per emit branch. They exist so a change to how
/// the generator builds its output cannot silently change what it emits: any difference shows up as a snapshot diff.
/// </summary>
public sealed class GeneratedSourceSnapshotTests
{
    [Fact]
    public Task should_emit_a_typed_dispatcher_for_every_consumer_shape()
    {
        return _VerifyGenerated(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Messaging;

            namespace Billing.Shapes;

            public interface IClock;

            public sealed record InvoiceIssued(string Number);
            public sealed record InvoicePaid(string Number);
            public sealed record PriceChanged(string Sku);
            public sealed record IssueInvoiceCommand(string OrderId);
            public sealed record CloseDay(int Day);
            public sealed record Rebuild(int Version);

            [BusConsumer("billing.invoice-projection")]
            public sealed class InvoiceProjection(IClock clock) : IConsume<InvoiceIssued>, IConsume<InvoicePaid>
            {
                public ValueTask ConsumeAsync(ConsumeContext<InvoiceIssued> context, CancellationToken cancellationToken) => default;
                public ValueTask ConsumeAsync(ConsumeContext<InvoicePaid> context, CancellationToken cancellationToken) => default;
            }

            [BusConsumer("billing.price-cache", EveryInstance = true)]
            public sealed class PriceCache : IConsume<PriceChanged>, IOnSubscriptionEstablished, IDisposable
            {
                public ValueTask ConsumeAsync(ConsumeContext<PriceChanged> context, CancellationToken cancellationToken) => default;
                public ValueTask OnSubscriptionEstablishedAsync(SubscriptionEstablishedContext context, CancellationToken cancellationToken) => default;
                public void Dispose() { }
            }

            [QueueConsumer("billing.issue-invoice")]
            internal sealed class IssueInvoice : IConsume<IssueInvoiceCommand>, IAsyncDisposable
            {
                ValueTask IConsume<IssueInvoiceCommand>.ConsumeAsync(ConsumeContext<IssueInvoiceCommand> context, CancellationToken cancellationToken) => default;
                public ValueTask DisposeAsync() => default;
            }

            [QueueConsumer("billing.close-day")]
            public sealed class CloseDayConsumer : IConsume<CloseDay>, IConsumerLifecycle
            {
                public ValueTask ConsumeAsync(ConsumeContext<CloseDay> context, CancellationToken cancellationToken) => default;
                public ValueTask OnStartingAsync(CancellationToken cancellationToken) => default;
                public ValueTask OnStoppingAsync(CancellationToken cancellationToken) => default;
            }

            [QueueConsumer("billing.rebuild")]
            public sealed class RebuildConsumer : IConsume<Rebuild>, IConsumerLifecycle, IAsyncDisposable
            {
                public ValueTask ConsumeAsync(ConsumeContext<Rebuild> context, CancellationToken cancellationToken) => default;
                public ValueTask OnStartingAsync(CancellationToken cancellationToken) => default;
                public ValueTask OnStoppingAsync(CancellationToken cancellationToken) => default;
                public ValueTask DisposeAsync() => default;
            }
            """
        );
    }

    [Fact]
    public Task should_emit_fully_qualified_names_for_nested_and_global_namespace_types()
    {
        return _VerifyGenerated(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Messaging;

            public sealed record GlobalMessage(int Id);

            [BusConsumer("root.global")]
            public sealed class GlobalConsumer : IConsume<GlobalMessage>
            {
                public ValueTask ConsumeAsync(ConsumeContext<GlobalMessage> context, CancellationToken cancellationToken) => default;
            }

            namespace Billing
            {
                public sealed record Payload(int Id);

                public static class Handlers
                {
                    [BusConsumer("billing.nested")]
                    public sealed class Nested : IConsume<Payload>, IConsume<GlobalMessage>
                    {
                        public ValueTask ConsumeAsync(ConsumeContext<Payload> context, CancellationToken cancellationToken) => default;
                        public ValueTask ConsumeAsync(ConsumeContext<GlobalMessage> context, CancellationToken cancellationToken) => default;
                    }
                }
            }

            namespace Billing_Handlers
            {
                [BusConsumer("billing.flattened")]
                public sealed class Nested : IConsume<Billing.Payload>
                {
                    public ValueTask ConsumeAsync(ConsumeContext<Billing.Payload> context, CancellationToken cancellationToken) => default;
                }
            }
            """
        );
    }

    [Fact]
    public Task should_emit_a_responder_registration_carrying_the_response_type_and_a_dispatcher_that_records_the_reply()
    {
        return _VerifyGenerated(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Messaging;

            namespace Pricing;

            public sealed record GetQuote(string Sku);
            public sealed record Quote(decimal Price);
            public sealed record RefreshPrices(int Version);
            public sealed record GetPriceList(string Region);
            public sealed record PriceList(int Count);

            [QueueConsumer("pricing.get-quote")]
            public sealed class GetQuoteResponder : IRespond<GetQuote, Quote>
            {
                public ValueTask<Quote> RespondAsync(ConsumeContext<GetQuote> context, CancellationToken cancellationToken) =>
                    new(new Quote(1m));
            }

            [QueueConsumer("pricing.desk")]
            public sealed class PricingDesk : IRespond<GetPriceList, PriceList>, IConsume<RefreshPrices>, IConsumerLifecycle, IAsyncDisposable
            {
                ValueTask<PriceList> IRespond<GetPriceList, PriceList>.RespondAsync(ConsumeContext<GetPriceList> context, CancellationToken cancellationToken) =>
                    new(new PriceList(2));
                public ValueTask ConsumeAsync(ConsumeContext<RefreshPrices> context, CancellationToken cancellationToken) => default;
                public ValueTask OnStartingAsync(CancellationToken cancellationToken) => default;
                public ValueTask OnStoppingAsync(CancellationToken cancellationToken) => default;
                public ValueTask DisposeAsync() => default;
            }
            """
        );
    }

    [Fact]
    public Task should_emit_a_failure_policy_factory_only_for_a_consumer_that_declares_one()
    {
        return _VerifyGenerated(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Messaging;
            using Headless.Reliability;

            namespace Billing.Policies;

            public sealed record ChargeCard(string OrderId);
            public sealed record CardCharged(string OrderId);
            public sealed record ReceiptPrinted(string OrderId);

            public sealed class PaymentsPolicy : FailurePolicy
            {
                protected override void Configure(FailurePolicyBuilder policy) => policy.Immediate(retries: 2);
            }

            [QueueConsumer("billing.charge", FailurePolicy = typeof(PaymentsPolicy))]
            public sealed class Charge : IConsume<ChargeCard>
            {
                public ValueTask ConsumeAsync(ConsumeContext<ChargeCard> context, CancellationToken cancellationToken) => default;
            }

            [BusConsumer("billing.ledger", FailurePolicy = typeof(PaymentsPolicy))]
            public sealed class Ledger : IConsume<CardCharged>
            {
                public ValueTask ConsumeAsync(ConsumeContext<CardCharged> context, CancellationToken cancellationToken) => default;
            }

            [BusConsumer("billing.receipts")]
            public sealed class Receipts : IConsume<ReceiptPrinted>
            {
                public ValueTask ConsumeAsync(ConsumeContext<ReceiptPrinted> context, CancellationToken cancellationToken) => default;
            }
            """
        );
    }

    private static Task _VerifyGenerated(string source)
    {
        var driver = GeneratorTestHelper.Run(source, out var diagnostics);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return Verify(driver).UseDirectory("Snapshots");
    }
}
