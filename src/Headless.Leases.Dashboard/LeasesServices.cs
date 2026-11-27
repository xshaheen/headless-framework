// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.DistributedLocks;
using Headless.Fencing;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Leases;

/// <summary>
/// The two halves the module reads, as the host registered them: the fenced-lease inspector and the unkeyed
/// distributed lock. Each half is optional, so the module serves whichever the host has.
/// </summary>
/// <remarks>
/// A <see cref="NullDistributedLock"/> counts as no lock provider: it holds nothing and lists nothing, so showing it
/// would read as a provider with no locks held.
/// </remarks>
internal sealed class LeasesServices
{
    public LeasesServices(IServiceProvider provider)
    {
        Argument.IsNotNull(provider);

        Inspector = provider.GetService<IFencedLeaseInspector>();
        Lock = ResolveLock(provider);
    }

    /// <summary>Gets the fenced-lease inspector, or <see langword="null"/> when the host runs no Fencing.</summary>
    public IFencedLeaseInspector? Inspector { get; }

    /// <summary>Gets the distributed lock, or <see langword="null"/> when the host registers no real provider.</summary>
    public IDistributedLock? Lock { get; }

    /// <summary>Gets whether the host serves the fenced-lease half.</summary>
    [MemberNotNullWhen(true, nameof(Inspector))]
    public bool HasFencing => Inspector is not null;

    /// <summary>Gets whether the host serves the distributed-lock half.</summary>
    [MemberNotNullWhen(true, nameof(Lock))]
    public bool HasLocks => Lock is not null;

    /// <summary>The unkeyed distributed lock, or <see langword="null"/> when none resolves or it is the do-nothing one.</summary>
    internal static IDistributedLock? ResolveLock(IServiceProvider provider) =>
        provider.GetService<IDistributedLock>() is { } candidate and not NullDistributedLock ? candidate : null;
}
