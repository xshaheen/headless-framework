// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

[PublicAPI]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DistributedMessageAttribute(string messageName) : Attribute
{
    public string MessageName { get; } = messageName;
}
