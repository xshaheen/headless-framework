// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public interface ILocalMessageEmitter
{
    void AddMessage(ILocalMessage messages);

    void ClearLocalMessages();

    IReadOnlyList<ILocalMessage> GetLocalMessages();
}
