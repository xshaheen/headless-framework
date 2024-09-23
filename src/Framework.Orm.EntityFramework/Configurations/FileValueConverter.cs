// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Text.Json;
using Framework.Kernel.BuildingBlocks.Constants;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using File = Framework.Kernel.Primitives.File;

namespace Framework.Orm.EntityFramework.Configurations;

public sealed class FileValueConverter()
    : ValueConverter<File?, string?>(
        v => JsonSerializer.Serialize(v, PlatformJsonConstants.DefaultInternalJsonOptions),
        v => v == null ? null : JsonSerializer.Deserialize<File>(v, PlatformJsonConstants.DefaultInternalJsonOptions)
    );
