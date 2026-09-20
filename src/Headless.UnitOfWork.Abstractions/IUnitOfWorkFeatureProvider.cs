// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork;

/// <summary>
/// Contributes one typed capability to a unit of work, so a bridge package can attach behavior the
/// unit-of-work packages know nothing about (enlisted publishing, for example) without either side
/// referencing the other.
/// </summary>
/// <remarks>
/// Register a provider with <c>services.AddUnitOfWorkFeature&lt;TProvider&gt;()</c>; the scoped manager reads
/// the registrations once and <see cref="IUnitOfWork.GetFeature{TFeature}" /> resolves the capability lazily
/// off the unit. At most one provider may claim a given <see cref="FeatureType" /> in a host.
/// <para>
/// <see cref="Create" /> runs at most once per unit, under the unit's lock, and the capability it returns is
/// cached on the unit for the unit's whole lifetime, then disposed with it (both terminal outcomes) when it
/// implements <see cref="IAsyncDisposable" /> or <see cref="IDisposable" />. A factory that registers
/// <see cref="IUnitOfWork.OnCompleted" /> on construction therefore registers exactly once.
/// </para>
/// </remarks>
[PublicAPI]
public interface IUnitOfWorkFeatureProvider
{
    /// <summary>
    /// The feature type <see cref="Create" /> produces; the key <see cref="IUnitOfWork.GetFeature{TFeature}" />
    /// matches against.
    /// </summary>
    Type FeatureType { get; }

    /// <summary>Creates the capability for <paramref name="unitOfWork" />.</summary>
    /// <remarks>
    /// <paramref name="unitOfWork" /> is the unit's root handle — never a nested view, which could complete
    /// while the unit stays active and leave the cached capability holding a view that can no longer carry
    /// work. It is safe to hold for the capability's lifetime.
    /// </remarks>
    /// <param name="unitOfWork">The unit the capability is attached to.</param>
    /// <returns>The capability; must be assignable to <see cref="FeatureType" />.</returns>
    object Create(IUnitOfWork unitOfWork);
}
