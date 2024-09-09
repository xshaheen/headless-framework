using System.Text.Json;
using Framework.Kernel.BuildingBlocks.Constants;
using Framework.Kernel.Domains;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace DotNetCore.CAP.Monitoring;

public static class CapMessageExtensions
{
    public static DistributedMessage<TPayload> GetPayloadMessage<TPayload>(this MessageDto message)
        where TPayload : IDistributedMessagePayload
    {
        var content = JsonSerializer.Deserialize<JsonElement>(
            message.Content!,
            PlatformJsonConstants.DefaultInternalJsonOptions
        );

        var payload = content
            .GetProperty("value")
            .Deserialize<DistributedMessage<TPayload>>(PlatformJsonConstants.DefaultInternalJsonOptions);

        return payload!;
    }

    public static TValue GetMessageValue<TValue>(this MessageDto message)
    {
        var content = JsonSerializer.Deserialize<JsonElement>(
            message.Content!,
            PlatformJsonConstants.DefaultInternalJsonOptions
        );
        var payload = content
            .GetProperty("value")
            .Deserialize<TValue>(PlatformJsonConstants.DefaultInternalJsonOptions);

        return payload!;
    }
}
