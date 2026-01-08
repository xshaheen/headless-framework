// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Monitoring;
using DotNetCore.CAP.Persistence;
using Framework.Domains;

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

        public async Task<MessageDto?> GetMessageAsync(
            string name,
            MessageType type = MessageType.Publish,
            string? status = nameof(StatusName.Succeeded)
        )
        {
            var messageQuery = new MessageQueryDto
            {
                Name = name,
                MessageType = type,
                StatusName = status,
                CurrentPage = 0,
                PageSize = 100,
            };

            var messages = await provider.GetMessageAsync(messageQuery);

            return messages.Items?.FirstOrDefault();
        }

        public async Task<PagedQueryResult<MessageDto>> GetMessageAsync(MessageQueryDto query)
        {
            var monitoringApi = provider.GetRequiredService<IDataStorage>().GetMonitoringApi();
            var messages = await monitoringApi.GetMessagesAsync(query);

            return messages;
        }
    }
}
