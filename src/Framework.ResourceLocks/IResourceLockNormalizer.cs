using Microsoft.Extensions.Options;

namespace Framework.ResourceLocks;

public interface IResourceLockNormalizer
{
    string NormalizeResource(string name);
}

public sealed class ResourceLockNormalizer(IOptions<ResourceLockOptions> options) : IResourceLockNormalizer
{
    private readonly ResourceLockOptions _options = options.Value;

    public string NormalizeResource(string name) => $"{_options.KeyPrefix}{name}";
}
