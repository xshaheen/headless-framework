using System.Text.Json;
using Framework.Kernel.Primitives;

namespace Framework.Kernel.BuildingBlocks.Abstractions;

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

public sealed class DefaultPrettySystemJsonSerializer : IJsonSerializer
{
    public string Serialize<T>(T obj)
    {
        return JsonSerializer.Serialize(obj, PlatformJsonConstants.DefaultPrettyJsonOptions);
    }

    public T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, PlatformJsonConstants.DefaultPrettyJsonOptions);
    }
}
