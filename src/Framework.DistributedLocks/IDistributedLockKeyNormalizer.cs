using Microsoft.Extensions.Options;

namespace Framework.DistributedLocks;

public interface IDistributedLockKeyNormalizer
{
    string NormalizeKey(string name);
}

public class DistributedLockKeyNormalizer : IDistributedLockKeyNormalizer
{
    protected DistributedLockOptions Options { get; }

    public DistributedLockKeyNormalizer(IOptions<DistributedLockOptions> options)
    {
        Options = options.Value;
    }

    public virtual string NormalizeKey(string name)
    {
        return $"{Options.KeyPrefix}{name}";
    }
}
