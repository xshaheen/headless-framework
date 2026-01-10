// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domain;
using Framework.Serializer;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace DotNetCore.CAP.Monitoring;

[PublicAPI]
public static class CapMessageExtensions
{
    private static readonly JsonSerializerOptions _JsonOptions = JsonConstants.DefaultInternalJsonOptions;

    extension(MessageDto message)
    {
        public DistributedMessage<TPayload> GetPayloadMessage<TPayload>()
        {
            var content = JsonSerializer.Deserialize<JsonElement>(message.Content!, _JsonOptions);

            var payload = content.GetProperty("value").Deserialize<DistributedMessage<TPayload>>(_JsonOptions);

            return payload!;
        }

        public TValue GetMessageValue<TValue>()
        {
            var content = JsonSerializer.Deserialize<JsonElement>(message.Content!, _JsonOptions);
            var payload = content.GetProperty("value").Deserialize<TValue>(_JsonOptions);

            return payload!;
        }
    }
}
