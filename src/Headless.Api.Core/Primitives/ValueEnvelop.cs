// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable CA2225, IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Primitives;

public sealed record ValueEnvelop<T>(T Data)
{
    public static implicit operator ValueEnvelop<T>(T operand) => new(operand);
}
