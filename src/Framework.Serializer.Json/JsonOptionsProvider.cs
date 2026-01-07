// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Serializer;

public interface IJsonOptionsProvider
{
    JsonSerializerOptions GetSerializeOptions();

    JsonSerializerOptions GetDeserializeOptions();
}

public sealed class DefaultJsonOptionsProvider : IJsonOptionsProvider
{
    public JsonSerializerOptions GetSerializeOptions()
    {
        return JsonConstants.DefaultWebJsonOptions;
    }

    public JsonSerializerOptions GetDeserializeOptions()
    {
        return JsonConstants.DefaultWebJsonOptions;
    }
}
