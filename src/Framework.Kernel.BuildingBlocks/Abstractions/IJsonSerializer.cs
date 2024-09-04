using System.Text.Json;
using Framework.Kernel.Primitives;

namespace Framework.Kernel.BuildingBlocks.Abstractions;

public interface IJsonSerializer
{
    string Serialize<T>(T obj);

    T? Deserialize<T>(string json);
}

public sealed class SystemJsonSerializer(JsonSerializerOptions? options = null) : IJsonSerializer
{
    public string Serialize<T>(T obj)
    {
        return JsonSerializer.Serialize(obj, options ?? PlatformJsonConstants.DefaultWebJsonOptions);
    }

    public T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, options ?? PlatformJsonConstants.DefaultWebJsonOptions);
    }
}
