// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Serializer;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using File = Framework.Primitives.File;

namespace Framework.Orm.EntityFramework.Configurations;

public sealed class FileValueConverter()
    : ValueConverter<File?, string?>(
        v => JsonSerializer.Serialize(v, JsonConstants.DefaultInternalJsonOptions),
        v => v == null ? null : JsonSerializer.Deserialize<File>(v, JsonConstants.DefaultInternalJsonOptions)
    );
