using Framework.BuildingBlocks.Primitives;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Database.EntityFramework.ValueConverters;

/// <summary>ValueConverter for <see cref = "UserId"/></summary>
public sealed class UserIdValueConverter : ValueConverter<UserId, string>
{
    public UserIdValueConverter()
        : base(v => v, v => v) { }

    public UserIdValueConverter(ConverterMappingHints? mappingHints = null)
        : base(v => v, v => v, mappingHints) { }
}
