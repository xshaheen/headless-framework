// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domains;
using Framework.Serializer;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace DotNetCore.CAP.Monitoring;

[PublicAPI]
public static class CapMessageExtensions
{
    public static DistributedMessage<TPayload> GetPayloadMessage<TPayload>(this MessageDto message)
    {
        var content = JsonSerializer.Deserialize<JsonElement>(
            message.Content!,
            JsonConstants.DefaultInternalJsonOptions
        );

        var payload = content
            .GetProperty("value")
            .Deserialize<DistributedMessage<TPayload>>(JsonConstants.DefaultInternalJsonOptions);

        return payload!;
    }

    public static TValue GetMessageValue<TValue>(this MessageDto message)
    {
        var content = JsonSerializer.Deserialize<JsonElement>(
            message.Content!,
            JsonConstants.DefaultInternalJsonOptions
        );
        var payload = content.GetProperty("value").Deserialize<TValue>(JsonConstants.DefaultInternalJsonOptions);

        return payload!;
    }
}
