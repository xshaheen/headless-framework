// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Monitoring;
using DotNetCore.CAP.Persistence;
using Framework.Domains;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class CapServiceProviderExtensions
{
    public static async Task<DistributedMessage<TPayload>?> GetPayloadMessageAsync<TPayload>(
        this IServiceProvider provider,
        string name,
        MessageType type = MessageType.Publish,
        string? status = nameof(StatusName.Succeeded)
    )
    {
        var message = await provider.GetMessageAsync(name, type, status);

        return message?.GetPayloadMessage<TPayload>();
    }

    public static async Task<MessageDto?> GetMessageAsync(
        this IServiceProvider provider,
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

    public static async Task<PagedQueryResult<MessageDto>> GetMessageAsync(
        this IServiceProvider provider,
        MessageQueryDto query
    )
    {
        var monitoringApi = provider.GetRequiredService<IDataStorage>().GetMonitoringApi();

        var messages = await monitoringApi.GetMessagesAsync(query);

        return messages;
    }
}
