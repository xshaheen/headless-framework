// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks;
using Framework.Domains;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace DotNetCore.CAP.Monitoring;

public static class CapMessageExtensions
{
    public static DistributedMessage<TPayload> GetPayloadMessage<TPayload>(this MessageDto message)
    {
        var content = JsonSerializer.Deserialize<JsonElement>(
            message.Content!,
            FrameworkJsonConstants.DefaultInternalJsonOptions
        );

        var payload = content
            .GetProperty("value")
            .Deserialize<DistributedMessage<TPayload>>(FrameworkJsonConstants.DefaultInternalJsonOptions);

        return payload!;
    }

    public static TValue GetMessageValue<TValue>(this MessageDto message)
    {
        var content = JsonSerializer.Deserialize<JsonElement>(
            message.Content!,
            FrameworkJsonConstants.DefaultInternalJsonOptions
        );
        var payload = content
            .GetProperty("value")
            .Deserialize<TValue>(FrameworkJsonConstants.DefaultInternalJsonOptions);

        return payload!;
    }
}
