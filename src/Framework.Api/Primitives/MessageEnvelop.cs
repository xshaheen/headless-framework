// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Primitives;

public interface IMessageEnvelop
{
    MessageDescriptor Message { get; }
}

public sealed record MessageEnvelop(MessageDescriptor Message) : IMessageEnvelop
{
    public static MessageEnvelop FromMessageDescriptor(MessageDescriptor operand) => new(operand);

    public static implicit operator MessageEnvelop(MessageDescriptor operand) => new(operand);

    public static MessageEnvelop FromString(string operand) => new(operand);

    public static implicit operator MessageEnvelop(string operand) => new(operand);
}
