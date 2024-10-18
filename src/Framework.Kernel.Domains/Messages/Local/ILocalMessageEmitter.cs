// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public interface ILocalMessageEmitter
{
    void AddMessage(ILocalMessage messages);

    void ClearLocalMessages();

    IReadOnlyList<ILocalMessage> GetLocalMessages();
}
