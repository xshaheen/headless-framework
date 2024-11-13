// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.BuildingBlocks.Helpers.Ids;
using Framework.Kernel.Checks;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

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
        public override ValueGenerator Create(IProperty property, ITypeBase typeBase)
        {
            Argument.IsNotNull(property);
            Argument.IsNotNull(typeBase);

            if (property.ValueGenerated is ValueGenerated.Never)
            {
                return base.Create(property, typeBase);
            }

            var propertyType = property.ClrType.UnwrapNullableType().UnwrapEnumType();

            if (propertyType == typeof(string))
            {
                //Generate temporary value if GetDefaultValueSql is set
                return new StringSequentialAtEndCompactGuidValueGenerator(property.GetDefaultValueSql() is not null);
            }

            return base.Create(property, typeBase);
        }
    }

    private sealed class StringSequentialAtEndCompactGuidValueGenerator(bool value) : ValueGenerator<string>
    {
        public override bool GeneratesTemporaryValues { get; } = value;

        public override string Next(EntityEntry entry) => SequentialGuid.NextSequentialAtEnd().ToString("N");
    }

    #endregion
}
