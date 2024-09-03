#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

[PublicAPI]
public interface IHasExtraProperties
{
    ExtraProperties ExtraProperties { get; }
}
