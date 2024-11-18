// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Domains;

[PublicAPI]
[AttributeUsage(AttributeTargets.Class)]
public sealed class LocalEventHandlerOrderAttribute(int order) : Attribute
{
    /// <summary>Handlers execute in ascending numeric value of the Order property.</summary>
    public int Order { get; } = order;
}
