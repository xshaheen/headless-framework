namespace Framework.Serializer;

public interface ISerializer
{
    T? Deserialize<T>(Stream data);

    void Serialize(object value, Stream output);
}

public interface ITextSerializer : ISerializer;

public interface IBinarySerializer : ISerializer;

public interface IJsonSerializer : ITextSerializer;
