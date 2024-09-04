using Microsoft.Extensions.Options;

namespace Framework.DistributedLocks;

public interface IDistributedLockKeyNormalizer
{
    string NormalizeKey(string name);
}

public sealed class DistributedLockKeyNormalizer : IDistributedLockKeyNormalizer
{
    private readonly DistributedLockOptions _options;

    public DistributedLockKeyNormalizer(IOptions<DistributedLockOptions> options)
    {
        _options = options.Value;
    }

    public string NormalizeKey(string name)
    {
        return $"{_options.KeyPrefix}{name}";
    }
}
