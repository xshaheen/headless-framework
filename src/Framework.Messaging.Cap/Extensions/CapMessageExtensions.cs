// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Text.Json;
using Framework.Kernel.BuildingBlocks;
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
