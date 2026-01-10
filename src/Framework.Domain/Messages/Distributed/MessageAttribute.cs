// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Domain;

[PublicAPI]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MessageAttribute(string messageName) : Attribute
{
    public string MessageName { get; } = messageName;
}
