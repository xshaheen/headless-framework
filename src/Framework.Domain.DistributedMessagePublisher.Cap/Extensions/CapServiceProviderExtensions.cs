// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNetCore.CAP.Monitoring;
using Framework.Domain;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Monitoring;
using Framework.Messages.Persistence;
using Framework.Primitives;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class CapServiceProviderExtensions
{
    extension(IServiceProvider provider)
    {
        public async Task<DistributedMessage<TPayload>?> GetPayloadMessageAsync<TPayload>(
            string name,
            MessageType type = MessageType.Publish,
            string? status = nameof(StatusName.Succeeded)
        )
        {
            var message = await provider.GetMessageAsync(name, type, status);

            return message?.GetPayloadMessage<TPayload>();
        }

        public async Task<MessageView?> GetMessageAsync(
            string name,
            MessageType type = MessageType.Publish,
            string? status = nameof(StatusName.Succeeded)
        )
        {
            var messageQuery = new MessageQuery
            {
                Name = name,
                MessageType = type,
                StatusName = status,
                CurrentPage = 0,
                PageSize = 100,
            };

            var messages = await provider.GetMessageAsync(messageQuery);

            foreach (var item in messages.Items)
            {
                return item;
            }

            return null;
        }

        public async Task<IndexPage<MessageView>> GetMessageAsync(MessageQuery query)
        {
            var monitoringApi = provider.GetRequiredService<IDataStorage>().GetMonitoringApi();
            var messages = await monitoringApi.GetMessagesAsync(query);

            return messages;
        }
    }
}
