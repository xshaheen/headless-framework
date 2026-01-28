// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Core;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

[PublicAPI]
public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Replace the default EF value generation for string primary keys from <c>Guid.ToString()</c> with hyphens to
    /// <c>SequentialGuid.NextSequentialAtEnd().ToString("N")</c>
    /// </summary>
    public static DbContextOptionsBuilder GenerateCompactNextSequentialAtEndGuidForPrimaryKeys(
        this DbContextOptionsBuilder builder
    )
    {
        builder.ReplaceService<IValueGeneratorSelector, CustomSequentialAtEndCompactGuidValueGeneratorSelector>();

        return builder;
    }

    #region Compact Guid

    private sealed class CustomSequentialAtEndCompactGuidValueGeneratorSelector(
        ValueGeneratorSelectorDependencies dependencies
    ) : RelationalValueGeneratorSelector(dependencies)
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
                valueGenerator = new StringSequentialAtEndCompactGuidValueGenerator(
                    property.GetDefaultValueSql() is not null
                );

                return true;
            }

            return base.TryCreate(property, typeBase, out valueGenerator);
        }
    }

    private sealed class StringSequentialAtEndCompactGuidValueGenerator(bool value) : ValueGenerator<string>
    {
        public override bool GeneratesTemporaryValues { get; } = value;

        public override string Next(EntityEntry entry)
        {
            return SequentialGuid.NextSequentialAtEnd().ToString("N");
        }
    }

    #endregion
}
