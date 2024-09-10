using Microsoft.Extensions.Options;

namespace Framework.DistributedLocks;

public interface IDistributedLockResourceNormalizer
{
    string Normalize(string name);
}

public sealed class DistributedLockResourceNormalizer(IOptions<DistributedLockOptions> options)
    : IDistributedLockResourceNormalizer
{
    private readonly DistributedLockOptions _options = options.Value;

    public string Normalize(string name) => $"{_options.KeyPrefix}{name}";
}
