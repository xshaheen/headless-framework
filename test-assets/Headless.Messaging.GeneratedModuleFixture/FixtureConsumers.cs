// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Reliability;

namespace Headless.Messaging.GeneratedModuleFixture;

public sealed record InvoiceIssued(string Number);

public sealed record IssueInvoiceCommand(string OrderId);

public sealed record LedgerEntryPosted(string Entry);

public sealed record LedgerEntryReversed(string Entry);

/// <summary>A failure policy type for the Bus consumer to name.</summary>
public sealed class FixtureFailurePolicy : IFailurePolicy;

/// <summary>Records what the generated dispatchers did, so a test can see each consumer ran with its dependencies.</summary>
public sealed class FixtureProbe
{
    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];

    // Deliveries through a transport record from consumer threads, so reads take a snapshot.
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    public void Record(string call)
    {
        lock (_lock)
        {
            _calls.Add(call);
        }
    }
}

[BusConsumer(Identity, EveryInstance = true, Policy = typeof(FixtureFailurePolicy))]
public sealed class InvoiceProjection(FixtureProbe probe)
    : IConsume<InvoiceIssued>,
        IOnSubscriptionEstablished,
        IDisposable
{
    public const string Identity = "fixture.invoice-projection";

    public ValueTask ConsumeAsync(ConsumeContext<InvoiceIssued> context, CancellationToken cancellationToken)
    {
        probe.Record($"projected {context.Message.Number} on {context.Lane}");
        return ValueTask.CompletedTask;
    }

    public ValueTask OnSubscriptionEstablishedAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public void Dispose() => probe.Record("projection disposed");
}

[QueueConsumer(Identity)]
public sealed class IssueInvoice(FixtureProbe probe) : IConsume<IssueInvoiceCommand>, IConsumerLifecycle
{
    public const string Identity = "fixture.issue-invoice";

    public ValueTask ConsumeAsync(ConsumeContext<IssueInvoiceCommand> context, CancellationToken cancellationToken)
    {
        probe.Record($"issued {context.Message.OrderId} on {context.Lane}");
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStartingAsync(CancellationToken cancellationToken)
    {
        probe.Record("issue starting");
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStoppingAsync(CancellationToken cancellationToken)
    {
        probe.Record("issue stopping");
        return ValueTask.CompletedTask;
    }
}

/// <summary>One identity covering two messages, so the host binds both to one subscription.</summary>
[BusConsumer(Identity)]
public sealed class LedgerProjection(FixtureProbe probe) : IConsume<LedgerEntryPosted>, IConsume<LedgerEntryReversed>
{
    public const string Identity = "fixture.ledger-projection";

    public ValueTask ConsumeAsync(ConsumeContext<LedgerEntryPosted> context, CancellationToken cancellationToken)
    {
        probe.Record($"posted {context.Message.Entry}");
        return ValueTask.CompletedTask;
    }

    public ValueTask ConsumeAsync(ConsumeContext<LedgerEntryReversed> context, CancellationToken cancellationToken)
    {
        probe.Record($"reversed {context.Message.Entry}");
        return ValueTask.CompletedTask;
    }
}
