using Framework.Arguments;
using Framework.BuildingBlocks.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Framework.Orm.EntityFramework.CompactGuidIds;

public sealed class StringCompactGuidValueGenerator(bool generateTemporaryValues) : ValueGenerator<string>
{
    public override bool GeneratesTemporaryValues { get; } = generateTemporaryValues;

    public override string Next(EntityEntry entry)
    {
        return SequentialGuid.NextSequentialAtEnd().ToString("N");
    }
}

public sealed class CustomRelationalValueGeneratorSelector(ValueGeneratorSelectorDependencies dependencies)
    : RelationalValueGeneratorSelector(dependencies)
{
    public override ValueGenerator Create(IProperty property, ITypeBase typeBase)
    {
        Argument.IsNotNull(property);
        Argument.IsNotNull(typeBase);

        if (property.ValueGenerated is not ValueGenerated.Never)
        {
            var propertyType = property.ClrType.UnwrapNullableType().UnwrapEnumType();

            if (propertyType == typeof(string))
            {
                //Generate temporary value if GetDefaultValueSql is set
                return new StringCompactGuidValueGenerator(property.GetDefaultValueSql() is not null);
            }
        }

        return base.Create(property, typeBase);
    }
}
