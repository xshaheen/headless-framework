// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public interface IDistributedMessageEmitter
{
    void AddMessage(IDistributedMessage message);

    void ClearDistributedMessages();

    IReadOnlyList<IDistributedMessage> GetDistributedMessages();
}
