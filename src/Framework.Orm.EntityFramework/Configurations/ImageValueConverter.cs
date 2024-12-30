// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;
using Framework.Serializer;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.Configurations;

public sealed class ImageValueConverter()
    : ValueConverter<Image?, string?>(
        v => JsonSerializer.Serialize(v, JsonConstants.DefaultInternalJsonOptions),
        v => v == null ? null : JsonSerializer.Deserialize<Image>(v, JsonConstants.DefaultInternalJsonOptions)
    );
