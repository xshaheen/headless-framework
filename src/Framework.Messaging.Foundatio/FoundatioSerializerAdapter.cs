// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Serializer;

namespace Framework.Messaging;

public sealed class FoundatioSerializerAdapter(IJsonSerializer serializer) : IFoundatioSerializer
{
    public object? Deserialize(Stream data, Type objectType)
    {
        return serializer.Deserialize(data, objectType);
    }

    public void Serialize(object value, Stream output)
    {
        serializer.Serialize(value, output);
    }
}
