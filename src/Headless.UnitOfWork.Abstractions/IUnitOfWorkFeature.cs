// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork;

/// <summary>
/// Marks a service that <see cref="IUnitOfWork.GetFeature{TFeature}" /> may hand out from a unit of work, so a
/// bridge package can expose behavior on the unit — enlisted publishing, for example — without the unit-of-work
/// packages referencing it.
/// </summary>
/// <remarks>
/// A feature is an ordinary service registered in the host container as a singleton; the unit resolves it from
/// the root provider and holds nothing per unit, so every unit sees the same instance, and a scoped or transient
/// registration is refused at <c>GetFeature</c>. Per-unit state belongs on the handle (<c>IUnitOfWork.GetOrAdd</c>),
/// which is how a feature's accessor binds to the unit it is called with. The marker is what keeps
/// <c>GetFeature</c> from becoming a general service locator: only types that opt in through this interface can be
/// reached from a unit.
/// </remarks>
[PublicAPI]
public interface IUnitOfWorkFeature;
