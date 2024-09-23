#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public interface IHasETag
{
    /// <summary>Raw version of the entity used for concurrency control</summary>
    byte[]? ETag { get; set; }
}
