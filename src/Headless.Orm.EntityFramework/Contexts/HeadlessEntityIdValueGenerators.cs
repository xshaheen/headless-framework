// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
    // Used only for a hand-constructed DbContextOptions with no application service provider (see
    // HeadlessEntityIdValueGeneration). Matches the framework default registration.
    private static readonly IGuidGenerator _Default = new SequentialAtEndGuidGenerator();

    // Cache the resolved generator per application service provider so Next() doesn't re-resolve from DI on
    // every inserted row. Keyed by provider (weak keys) to stay correct across independent hosts in one process.
    private static readonly ConditionalWeakTable<IServiceProvider, IGuidGenerator> _ByProvider = [];

    private static readonly ConditionalWeakTable<IServiceProvider, IGuidGenerator>.CreateValueCallback _Resolve =
        static provider => provider.GetService<IGuidGenerator>() ?? _Default;

    // The generated value is the framework's source of truth, emitted in the INSERT — never a placeholder the
    // store is expected to replace.
    public override bool GeneratesTemporaryValues => false;

    public override Guid Next(EntityEntry entry)
    {
        var applicationServices = HeadlessEntityIdValueGeneration.GetApplicationServices(entry.Context);

        var generator = applicationServices is null ? _Default : _ByProvider.GetValue(applicationServices, _Resolve);

        return generator.Create();
    }
}

internal static class HeadlessEntityIdValueGeneration
{
    /// <summary>
    /// The application service provider EF Core was built with (set by the Headless registration via
    /// <c>UseApplicationServiceProvider</c>), or <c>null</c> for a hand-constructed <c>DbContextOptions</c>
    /// outside the DI pipeline — in which case the generators fall back to their framework default.
    /// </summary>
    internal static IServiceProvider? GetApplicationServices(DbContext context)
    {
        return context
            .GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()
            ?.ApplicationServiceProvider;
    }
}
