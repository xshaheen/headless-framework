// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Headless.EntityFramework.Contexts;

/// <summary>
/// EF Core value generator that produces an application-owned <see cref="Guid"/> key from the host's registered
/// <see cref="IGuidGenerator"/>. Attached (paired with <c>ValueGenerated.Never</c>) by
/// <c>ConfigureHeadlessValueGenerated</c>, so EF Core generates the key during the add transition — before
/// <c>SaveChanges</c> and without a store identity column or a database round-trip.
/// </summary>
internal sealed class HeadlessGuidIdValueGenerator : ValueGenerator<Guid>
{
    // The generated value is the framework's source of truth, emitted in the INSERT — never a placeholder the
    // store is expected to replace.
    public override bool GeneratesTemporaryValues => false;

    public override Guid Next(EntityEntry entry)
    {
        return HeadlessGuidGeneratorResolver.Resolve(entry.Context).Create();
    }
}
