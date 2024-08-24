using Framework.BuildingBlocks.Primitives;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.Configurations;

/// <summary>ValueConverter for <see cref = "UserId"/></summary>
public sealed class UserIdValueConverter : ValueConverter<UserId, string>
{
    public UserIdValueConverter()
        : base(v => v, v => v) { }

    public UserIdValueConverter(ConverterMappingHints? mappingHints = null)
        : base(v => v, v => v, mappingHints) { }
}
