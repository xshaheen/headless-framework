// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Serializer;

namespace Framework.Features.FeatureManagement;

public class StringValueTypeSerializer
{
    protected IJsonSerializer JsonSerializer { get; }

    public StringValueTypeSerializer(IJsonSerializer jsonSerializer)
    {
        JsonSerializer = jsonSerializer;
    }

    public virtual string Serialize(IStringValueType stringValueType)
    {
        return JsonSerializer.Serialize(stringValueType);
    }

    public virtual IStringValueType Deserialize(string value)
    {
        return JsonSerializer.Deserialize<IStringValueType>(value);
    }
}
