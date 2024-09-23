// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

public sealed record DataEnvelop<T>(T Data, List<OperationDescriptor>? Operations = null)
{
    public static implicit operator DataEnvelop<T>(T operand) => new(operand);

    public DataEnvelop<T> FromT() => this;
}
