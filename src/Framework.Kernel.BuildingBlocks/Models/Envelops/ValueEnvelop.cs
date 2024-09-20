#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

public sealed record ValueEnvelop<T>(T Data)
{
    public static implicit operator ValueEnvelop<T>(T operand) => new(operand);

    public ValueEnvelop<T> FromT() => this;
}
