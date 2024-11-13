// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Framework.Kernel.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using File = Framework.Kernel.Primitives.File;

namespace Framework.Orm.EntityFramework.Configurations;

public sealed class FileValueConverter()
    : ValueConverter<File?, string?>(
        v => JsonSerializer.Serialize(v, FrameworkJsonConstants.DefaultInternalJsonOptions),
        v => v == null ? null : JsonSerializer.Deserialize<File>(v, FrameworkJsonConstants.DefaultInternalJsonOptions)
    );
