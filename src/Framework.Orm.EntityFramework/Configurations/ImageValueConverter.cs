using System.Text.Json;
using Framework.Kernel.BuildingBlocks.Constants;
using Framework.Kernel.Primitives;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.Configurations;

public sealed class ImageValueConverter()
    : ValueConverter<Image?, string?>(
        v => JsonSerializer.Serialize(v, PlatformJsonConstants.DefaultInternalJsonOptions),
        v => v == null ? null : JsonSerializer.Deserialize<Image>(v, PlatformJsonConstants.DefaultInternalJsonOptions)
    );
