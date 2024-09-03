#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

[PublicAPI]
public sealed record OperationDescriptor(string Code, string Href, string Method, string IdempotentKey);
