#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public interface IMultiTenant<out TId>
{
    /// <summary>ID of the related tenant.</summary>
    TId TenantId { get; }
}
