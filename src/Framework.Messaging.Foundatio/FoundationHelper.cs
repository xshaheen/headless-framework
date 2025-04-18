// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Foundatio.Serializer;
using Framework.Serializer;
using ISerializer = Foundatio.Serializer.ISerializer;

namespace Framework.Messaging;

public static class FoundationHelper
{
    public static ISerializer JsonSerializer =>
        new SystemTextJsonSerializer(
            serializeOptions: JsonConstants.DefaultInternalJsonOptions,
            deserializeOptions: JsonConstants.DefaultInternalJsonOptions
        );
}
