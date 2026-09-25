// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.MultiTenancy;
using Headless.Sequences;
using Headless.UnitOfWork;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>Builds the Core sequence services over a substituted store and a settable tenant.</summary>
internal sealed class SequenceTestContext
{
    public SequenceTestContext()
    {
        var monitor = Substitute.For<IOptionsMonitor<SequencesOptions>>();
        monitor.CurrentValue.Returns(_ => Options);

        Resolver = new SequenceRequestResolver(Tenant, monitor);
        Generator = new SequenceGenerator(Resolver, Store);
        Feature = new UnitOfWorkSequencesFeature(Resolver, Store);

        Store
            .IncrementAsync(Arg.Any<SequenceKey>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call => ValueTask.FromResult(call.ArgAt<long>(1)));
        Store
            .IncrementEnlistedAsync(
                Arg.Any<IRelationalUnitOfWorkResource>(),
                Arg.Any<SequenceKey>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => ValueTask.FromResult(call.ArgAt<long>(2)));
    }

    public SequencesOptions Options { get; } = new();

    public MutableCurrentTenant Tenant { get; } = new();

    public ISequenceStore Store { get; } = Substitute.For<ISequenceStore>();

    public SequenceRequestResolver Resolver { get; }

    public SequenceGenerator Generator { get; }

    public UnitOfWorkSequencesFeature Feature { get; }

    public SequenceTestContext GapFree(string name)
    {
        Options.Policies[name] = new SequencePolicy { Mode = SequenceMode.GapFree };

        return this;
    }

    /// <summary>An active unit over a live relational resource: every refusal gate passes.</summary>
    public static (IUnitOfWork Unit, IRelationalUnitOfWorkResource Resource) ActiveUnit(bool isOwned = true)
    {
        var resource = Substitute.For<IRelationalUnitOfWorkResource>();
        resource.IsOwned.Returns(isOwned);
        resource.IsTransactionCompleted.Returns(false);
        resource.Transaction.Returns(Substitute.For<DbTransaction>());

        var unit = Substitute.For<IUnitOfWork>();
        unit.State.Returns(UnitOfWorkState.Active);
        unit.Resource.Returns(resource);

        return (unit, resource);
    }
}

internal sealed class MutableCurrentTenant : ICurrentTenant
{
    public bool IsAvailable => Id is not null;

    public string? Id { get; set; }

    public string? Name => null;

    public IDisposable Change(string? id, string? name = null)
    {
        var previous = Id;
        Id = id;

        return new Restore(this, previous);
    }

    private sealed class Restore(MutableCurrentTenant tenant, string? previous) : IDisposable
    {
        public void Dispose() => tenant.Id = previous;
    }
}
