// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork;

/// <summary>
/// Marks a service that <see cref="IUnitOfWork.GetFeature{TFeature}" /> may hand out from a unit of work, so a
/// bridge package can expose behavior on the unit — enlisted publishing, for example — without the unit-of-work
/// packages referencing it.
/// </summary>
/// <remarks>
/// A feature is an ordinary service registered in the host container; the unit resolves it from the scope that
/// owns the unit's manager and holds nothing per unit. The marker is what keeps <c>GetFeature</c> from becoming a
/// general service locator: only types that opt in through this interface can be reached from a unit.
/// </remarks>
[PublicAPI]
public interface IUnitOfWorkFeature;
