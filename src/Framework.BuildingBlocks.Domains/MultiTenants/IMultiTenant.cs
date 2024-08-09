// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks.Domains;

public interface IMultiTenant<out TId>
{
    /// <summary>ID of the related tenant.</summary>
    TId TenantId { get; }
}
