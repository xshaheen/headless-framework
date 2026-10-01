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

    private static Task _VerifyGenerated(string source)
    {
        var driver = GeneratorTestHelper.Run(source, out var diagnostics);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return Verify(driver).UseDirectory("Snapshots");
    }
}
