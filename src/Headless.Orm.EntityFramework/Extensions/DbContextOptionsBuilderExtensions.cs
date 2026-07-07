// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

[PublicAPI]
public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Replaces EF Core's default string primary-key value generation (a hyphenated <c>Guid.ToString()</c>) with a
    /// compact, no-hyphen guid (<c>ToString("N")</c>) produced by the application's registered
    /// <see cref="IGuidGenerator"/>. SQL Server contexts use the <see cref="SequentialGuidType.SqlServer"/> comb;
    /// other providers use <see cref="SequentialGuidType.Version7"/>. The compact <c>"N"</c> format is always
    /// guaranteed.
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
        private const string _SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";

        private static readonly IGuidGenerator _Version7GuidGenerator = new SequentialGuidGenerator(
            SequentialGuidType.Version7
        );
        private static readonly IGuidGenerator _SqlServerGuidGenerator = new SequentialGuidGenerator(
            SequentialGuidType.SqlServer
        );

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
            var key = _GetKey(context.Database.ProviderName);
            var fallback = _GetFallback(key);
            var applicationServiceProvider = context
                .GetService<IDbContextOptions>()
                .FindExtension<CoreOptionsExtension>()
                ?.ApplicationServiceProvider;

            if (applicationServiceProvider is null)
            {
                return fallback;
            }

            return applicationServiceProvider.GetKeyedService<IGuidGenerator>(key)
                ?? applicationServiceProvider.GetService<IGuidGenerator>()
                ?? fallback;
        }

        private static SequentialGuidType _GetKey(string? providerName) =>
            string.Equals(providerName, _SqlServerProviderName, StringComparison.Ordinal)
                ? SequentialGuidType.SqlServer
                : SequentialGuidType.Version7;

        private static IGuidGenerator _GetFallback(SequentialGuidType key) =>
            key == SequentialGuidType.SqlServer ? _SqlServerGuidGenerator : _Version7GuidGenerator;
    }

    #endregion
}
