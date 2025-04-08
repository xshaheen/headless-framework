// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Serializer;

public interface ISerializer
{
    T? Deserialize<T>(Stream data);

    void Serialize<T>(T value, Stream output);

    object? Deserialize(Stream data, Type objectType);

    void Serialize(object? value, Stream output);
}

public interface ITextSerializer : ISerializer;

public interface IBinarySerializer : ISerializer;

public interface IJsonSerializer : ITextSerializer;
