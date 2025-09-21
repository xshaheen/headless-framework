// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Foundatio.Serializer;
using Framework.Serializer;
using ITextSerializer = Foundatio.Serializer.ITextSerializer;

namespace Framework.Messaging;

public static class FoundationHelper
{
    public static ITextSerializer JsonSerializer =>
        new SystemTextJsonSerializer(
            serializeOptions: JsonConstants.DefaultInternalJsonOptions,
            deserializeOptions: JsonConstants.DefaultInternalJsonOptions
        );
}
