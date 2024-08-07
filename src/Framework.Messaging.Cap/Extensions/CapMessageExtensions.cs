using System.Text.Json;
using Framework.BuildingBlocks.Constants;
using Framework.BuildingBlocks.Domains;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace DotNetCore.CAP.Monitoring;

public static class CapMessageExtensions
{
    public static PayloadDistributedMessage<TPayload> GetPayloadMessage<TPayload>(this MessageDto message)
        where TPayload : IDistributedMessagePayload
    {
        var content = JsonSerializer.Deserialize<JsonElement>(
            message.Content!,
            PlatformJsonConstants.DefaultInternalJsonOptions
        );

        var payload = content
            .GetProperty("value")
            .Deserialize<PayloadDistributedMessage<TPayload>>(PlatformJsonConstants.DefaultInternalJsonOptions);

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
