// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Idempotency;

/// <summary>
/// Implemented by a store that may refuse to draw an attempt's generation inside a caller's unit of work, because a
/// generation drawn in a transaction that then rolls back could be issued again.
/// </summary>
internal interface IIdempotencyEnlistedAdmissionGuard
{
    /// <summary>Throws when an admission cannot run inside <paramref name="unitOfWork" />.</summary>
    /// <exception cref="NotSupportedException">The store cannot draw a generation inside a caller's transaction.</exception>
    void ValidateEnlistedAdmission(IUnitOfWork unitOfWork);
}
