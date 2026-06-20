// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Primitives;

public sealed record DataEnvelop<T>(T Data)
{
    public static implicit operator DataEnvelop<T>(T operand) => new(operand);

    public DataEnvelop<T> FromT() => this;
}
