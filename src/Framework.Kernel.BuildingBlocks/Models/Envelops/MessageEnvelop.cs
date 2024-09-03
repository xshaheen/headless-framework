using Framework.Kernel.Primitives;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks;

public interface IMessageEnvelop
{
    MessageDescriptor Message { get; }
}

public sealed record MessageEnvelop(MessageDescriptor Message) : IMessageEnvelop
{
    public static MessageEnvelop FromMessageDescriptor(MessageDescriptor operand) => new(operand);

    public static implicit operator MessageEnvelop(MessageDescriptor operand) => new(operand);
}
