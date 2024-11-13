// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

[PublicAPI]
public interface IHasExtraProperties
{
    ExtraProperties ExtraProperties { get; }
}
