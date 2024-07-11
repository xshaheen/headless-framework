using System.Text.Json;
using Framework.BuildingBlocks.Constants;

namespace Framework.BuildingBlocks.Abstractions;

public interface IJsonSerializer
{
    string Serialize<T>(T obj);

    T? Deserialize<T>(string json);
}

public sealed class DefaultWebSystemJsonSerializer : IJsonSerializer
{
    public string Serialize<T>(T obj)
    {
        return JsonSerializer.Serialize(obj, PlatformJsonConstants.DefaultWebJsonOptions);
    }

    public T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, PlatformJsonConstants.DefaultWebJsonOptions);
    }
}

public sealed class DefaultInternalSystemJsonSerializer : IJsonSerializer
{
    public string Serialize<T>(T obj)
    {
        return JsonSerializer.Serialize(obj, PlatformJsonConstants.DefaultInternalJsonOptions);
    }

    public T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, PlatformJsonConstants.DefaultInternalJsonOptions);
    }
}
