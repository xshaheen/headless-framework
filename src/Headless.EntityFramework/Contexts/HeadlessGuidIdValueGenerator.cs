// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework.Contexts;

/// <summary>
/// EF Core value generator that produces an application-owned <see cref="Guid"/> key from the host's registered
/// <see cref="IGuidGenerator"/>. Attached (paired with <c>ValueGenerated.Never</c>) by
/// <c>ConfigureHeadlessValueGenerated</c>, so EF Core generates the key during the add transition — before
/// <c>SaveChanges</c> and without a store identity column or a database round-trip.
/// </summary>
internal sealed class HeadlessGuidIdValueGenerator : ValueGenerator<Guid>
{
    private const string _SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";

    // Used when a context was built without an application service provider (see HeadlessEntityIdValueGeneration).
    // The fallback still follows the EF provider's GUID sort order.
    private static readonly IGuidGenerator _Version7Generator = new SequentialGuidGenerator(
        SequentialGuidType.Version7
    );

    private static readonly IGuidGenerator _SqlServerGenerator = new SequentialGuidGenerator(
        SequentialGuidType.SqlServer
    );

    // The generated value is the framework's source of truth, emitted in the INSERT — never a placeholder the
    // store is expected to replace.
    public override bool GeneratesTemporaryValues => false;

    // Resolved generator per (application service provider, key): the keyed/unregistered answer is fixed for a
    // built provider, so bulk inserts used to repeat two DI lookups and a provider-name comparison per row.
    private readonly ConcurrentDictionary<
        (IServiceProvider Services, SequentialGuidType Key),
        IGuidGenerator?
    > _resolved = new();

    public override Guid Next(EntityEntry entry)
    {
        var key = _GetKey(entry.Context.Database.ProviderName);
        var fallback = _GetFallback(key);
        var applicationServices = entry.Context.GetApplicationServices();

        if (applicationServices is null)
        {
            return fallback.Create();
        }

        var generator = _resolved.GetOrAdd(
            (applicationServices, key),
            static state =>
                state.Services.GetKeyedService<IGuidGenerator>(state.Key) ?? state.Services.GetService<IGuidGenerator>()
        );

        return (generator ?? fallback).Create();
    }

    private static SequentialGuidType _GetKey(string? providerName)
    {
        return string.Equals(providerName, _SqlServerProviderName, StringComparison.Ordinal)
            ? SequentialGuidType.SqlServer
            : SequentialGuidType.Version7;
    }

    private static IGuidGenerator _GetFallback(SequentialGuidType key)
    {
        return key == SequentialGuidType.SqlServer ? _SqlServerGenerator : _Version7Generator;
    }
}
