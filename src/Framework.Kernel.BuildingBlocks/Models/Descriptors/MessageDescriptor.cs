#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

[PublicAPI]
public sealed record MessageDescriptor(string Code, [LocalizationRequired] string Description)
{
    public static implicit operator MessageDescriptor(string description) => new(description, description);

    public static MessageDescriptor ToMessageDescriptor(string description) => description;
}
