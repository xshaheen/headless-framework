// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Domain;

public interface IDistributedMessage
{
    string UniqueId { get; }
}
