// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public interface IMultiTenant<out TId>
{
    /// <summary>ID of the related tenant.</summary>
    TId TenantId { get; }
}
