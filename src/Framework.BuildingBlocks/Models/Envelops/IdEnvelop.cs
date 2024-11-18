// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable CA2225, IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Primitives;

public interface IIdEnvelop
{
    string Id { get; }
}

public sealed record IdEnvelop(string Id) : IIdEnvelop
{
    public static implicit operator IdEnvelop(string operand) => new(operand);

    public static implicit operator IdEnvelop(Guid operand) => new(operand.ToString());

    public static implicit operator IdEnvelop(int operand) => new(operand.ToString(CultureInfo.InvariantCulture));

    public static implicit operator IdEnvelop(long operand) => new(operand.ToString(CultureInfo.InvariantCulture));
}
