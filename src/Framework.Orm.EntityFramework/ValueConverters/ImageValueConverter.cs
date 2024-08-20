using System.Text.Json;
using Framework.BuildingBlocks.Constants;
using Framework.BuildingBlocks.Primitives;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.ValueConverters;

public sealed class ImageValueConverter()
    : ValueConverter<Image?, string?>(
        v => JsonSerializer.Serialize(v, PlatformJsonConstants.DefaultInternalJsonOptions),
        v => v == null ? null : JsonSerializer.Deserialize<Image>(v, PlatformJsonConstants.DefaultInternalJsonOptions)
    );
