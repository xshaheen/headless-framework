// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Checks;
using Headless.EntityFramework.Contexts;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Microsoft.EntityFrameworkCore;

[PublicAPI]
public static class HeadlessDbContextOptionsBuilderExtensions
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
        public override bool GeneratesTemporaryValues { get; } = value;

        // String primary keys share the framework's single guid source (fakeable in tests, swappable for a different
        // strategy), with the framework default when the context has no application service provider.
        public override string Next(EntityEntry entry)
        {
            return HeadlessGuidGeneratorResolver.Resolve(entry.Context).Create().ToString("N");
        }
    }

    #endregion
}
