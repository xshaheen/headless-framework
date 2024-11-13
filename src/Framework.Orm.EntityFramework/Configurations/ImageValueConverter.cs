// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Framework.Kernel.BuildingBlocks;
using Framework.Kernel.Primitives;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.Configurations;

public sealed class ImageValueConverter()
    : ValueConverter<Image?, string?>(
        v => JsonSerializer.Serialize(v, FrameworkJsonConstants.DefaultInternalJsonOptions),
        v => v == null ? null : JsonSerializer.Deserialize<Image>(v, FrameworkJsonConstants.DefaultInternalJsonOptions)
    );
