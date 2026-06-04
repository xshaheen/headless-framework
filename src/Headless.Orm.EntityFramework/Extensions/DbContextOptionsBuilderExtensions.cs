// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Abstractions;
using Headless.Checks;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

[PublicAPI]
public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Replaces EF Core's default string primary-key value generation (a hyphenated <c>Guid.ToString()</c>) with a
    /// compact, no-hyphen guid (<c>ToString("N")</c>) produced by the application's registered
    /// <see cref="IGuidGenerator"/>. The framework default is <see cref="SequentialAtEndGuidGenerator"/>, so keys
    /// are sequential-at-end out of the box, but the generation strategy follows whichever
    /// <see cref="IGuidGenerator"/> the host registers. The compact <c>"N"</c> format is always guaranteed.
    /// </summary>
    public static DbContextOptionsBuilder GenerateCompactGuidForStringPrimaryKeys(this DbContextOptionsBuilder builder)
    {
        builder.ReplaceService<IValueGeneratorSelector, CompactGuidValueGeneratorSelector>();

        return builder;
    }

    #region Compact Guid

    private sealed class CompactGuidValueGeneratorSelector(ValueGeneratorSelectorDependencies dependencies)
        : RelationalValueGeneratorSelector(dependencies)
    {
        public override bool TryCreate(IProperty property, ITypeBase typeBase, out ValueGenerator? valueGenerator)
        {
            Argument.IsNotNull(property);
            Argument.IsNotNull(typeBase);

            if (property.ValueGenerated is ValueGenerated.Never)
            {
                return base.TryCreate(property, typeBase, out valueGenerator);
            }

            var propertyType = property.ClrType.UnwrapNullableType().UnwrapEnumType();

            if (propertyType == typeof(string))
            {
                // Generate temporary value if GetDefaultValueSql is set
                valueGenerator = new StringCompactGuidValueGenerator(property.GetDefaultValueSql() is not null);

                return true;
            }

            return base.TryCreate(property, typeBase, out valueGenerator);
        }
    }

    private sealed class StringCompactGuidValueGenerator(bool value) : ValueGenerator<string>
    {
        private static readonly IGuidGenerator _DefaultGuidGenerator = new SequentialAtEndGuidGenerator();

        // Cache the resolved generator per application service provider so Next() doesn't re-resolve from DI
        // on every inserted row. Keyed by provider (not a single field) to stay correct when independent hosts
        // in one process use different providers; weak keys avoid pinning providers alive.
        private static readonly ConditionalWeakTable<IServiceProvider, IGuidGenerator> _GuidGeneratorByProvider = [];

        public override bool GeneratesTemporaryValues { get; } = value;

        public override string Next(EntityEntry entry)
        {
            return _ResolveGuidGenerator(entry.Context).Create().ToString("N");
        }

        // Pull the application-registered IGuidGenerator so string primary keys share the framework's single
        // guid source (fakeable in tests, swappable for a different strategy). Falls back to the framework
        // default when the context was built without an application service provider (e.g. a hand-constructed
        // DbContextOptions).
        private static IGuidGenerator _ResolveGuidGenerator(DbContext context)
        {
            var applicationServiceProvider = context
                .GetService<IDbContextOptions>()
                .FindExtension<CoreOptionsExtension>()
                ?.ApplicationServiceProvider;

            if (applicationServiceProvider is null)
            {
                return _DefaultGuidGenerator;
            }

            return _GuidGeneratorByProvider.GetValue(
                applicationServiceProvider,
                static provider => provider.GetService<IGuidGenerator>() ?? _DefaultGuidGenerator
            );
        }
    }

    #endregion
}
