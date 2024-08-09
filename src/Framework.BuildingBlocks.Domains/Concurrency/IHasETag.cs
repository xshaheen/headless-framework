// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks.Domains;

public interface IHasETag
{
    /// <summary>Raw version of the entity used for concurrency control</summary>
    byte[]? ETag { get; set; }
}
